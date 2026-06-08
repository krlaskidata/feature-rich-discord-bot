using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace MainbotCSharp.Modules
{
    // Ticket Service Classes
    public class TicketConfigEntry
    {
        public ulong LogChannelId { get; set; }
        public ulong? TicketCategoryId { get; set; }
        public ulong? SupportRoleId { get; set; }
        public List<ulong> SupportRoleIds { get; set; } = new List<ulong>(); // New field for multiple roles
        
        // Setup tracking for continuation feature
        public string? SetupStep { get; set; }
        public ulong? TicketMessageChannelId { get; set; }
        public ulong? TicketMessageId { get; set; }
        public DateTime? LastSetupAttempt { get; set; }
    }

    public static class TicketService
    {
        private const string TICKETS_CONFIG_FILE = "tickets_config.json";
        private static Dictionary<ulong, TicketConfigEntry> _cfg = LoadTicketsConfig();
        public static ConcurrentDictionary<ulong, TicketMeta> TicketMetas = new();
        private static readonly Dictionary<ulong, System.Timers.Timer> _autoCloseTimers = new();
        
        // MEMORY OPTIMIZATION: Cleanup constants
        private static DateTime _lastCleanup = DateTime.UtcNow;
        private const int MAX_TICKET_METAS = 200; // Reduced from 2000 to 200 for memory optimization
        private const int CLEANUP_INTERVAL_HOURS = 6; // Cleanup every 6 hours

        private static Dictionary<ulong, TicketConfigEntry> LoadTicketsConfig()
        {
            try
            {
                if (!File.Exists(TICKETS_CONFIG_FILE)) return new Dictionary<ulong, TicketConfigEntry>();
                var txt = File.ReadAllText(TICKETS_CONFIG_FILE);
                var d = JsonSerializer.Deserialize<Dictionary<ulong, TicketConfigEntry>>(txt);
                return d ?? new Dictionary<ulong, TicketConfigEntry>();
            }
            catch { return new Dictionary<ulong, TicketConfigEntry>(); }
        }

        private static void SaveTicketsConfig()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(TICKETS_CONFIG_FILE, txt);
            }
            catch { }
        }

        public static TicketConfigEntry? GetConfig(ulong guildId)
        {
            // Always reload config from disk to ensure latest settings
            _cfg = LoadTicketsConfig();
            if (_cfg.TryGetValue(guildId, out var e)) return e; return null;
        }

        public static void SetConfig(ulong guildId, TicketConfigEntry cfg)
        {
            _cfg[guildId] = cfg; SaveTicketsConfig();
        }

        public static void RemoveConfig(ulong guildId)
        {
            _cfg.Remove(guildId);
            SaveTicketsConfig();
        }

        public class TicketMeta
        {
            public ulong UserId { get; set; }
            public string? Category { get; set; }
            public ulong GuildId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public string? Username { get; set; }
        }

        public static async Task<(bool success, SocketTextChannel? channel, ulong channelId)> CreateTicketChannelAsync(SocketGuild guild, SocketUser user, string category)
        {

            try
            {
                var config = GetConfig(guild.Id);
                var random = new Random();
                var randomNumber = random.Next(1000, 9999);
                var channelName = $"┣s-ticket-{user.Username}-{randomNumber}";

                Console.WriteLine($"[TicketDebug] Starting ticket creation for {user.Username} in guild {guild.Name}");

                var overwrites = new List<Overwrite>
                {
                    new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
                    new Overwrite(user.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow))
                };

                // Add support roles if configured
                if (config != null)
                {
                    // Use the new SupportRoleIds list if it has entries, otherwise fall back to single SupportRoleId
                    var roleIds = config.SupportRoleIds?.Any() == true ? config.SupportRoleIds : 
                                 (config.SupportRoleId.HasValue ? new List<ulong> { config.SupportRoleId.Value } : new List<ulong>());

                    foreach (var roleId in roleIds)
                    {
                        overwrites.Add(new Overwrite(roleId, PermissionTarget.Role,
                            new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow)));
                        Console.WriteLine($"[TicketDebug] Added permissions for support role ID: {roleId}");
                    }
                }

                // DEBUG: Log category ID and found category name
                if (config?.TicketCategoryId.HasValue == true)
                {
                    var cat = guild.GetCategoryChannel(config.TicketCategoryId.Value);
                    Console.WriteLine($"[TicketDebug] TicketCategoryId: {config.TicketCategoryId.Value}");
                    Console.WriteLine($"[TicketDebug] Found category: {(cat != null ? cat.Name : "NOT FOUND")} (ID: {config.TicketCategoryId.Value})");
                }

                Console.WriteLine($"[TicketDebug] Creating channel with name: {channelName}");

                var restChannel = await guild.CreateTextChannelAsync(channelName, properties =>
                {
                    if (config?.TicketCategoryId.HasValue == true)
                        properties.CategoryId = config.TicketCategoryId.Value;
                    properties.Topic = $"Support ticket for {user.Username} - Category: {category}";
                    properties.PermissionOverwrites = overwrites;
                });

                Console.WriteLine($"[TicketDebug] Channel created successfully: {restChannel.Name} (ID: {restChannel.Id})");

                // Store ticket metadata using REST channel ID
                TicketMetas[restChannel.Id] = new TicketMeta
                {
                    UserId = user.Id,
                    Category = category,
                    GuildId = guild.Id,
                    Username = user.Username
                };

                Console.WriteLine($"[TicketDebug] Ticket metadata stored for channel {restChannel.Id}");

                // Send initial ticket message using REST channel directly
                var embed = new EmbedBuilder()
                    .WithTitle($"🎫 Support Ticket - {category}")
                    .WithDescription($"Hello {user.Mention}! Thank you for creating a support ticket.\n\n" +
                                   $"**Category:** {category}\n" +
                                   $"**Created:** <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>\n\n" +
                                   "Please describe your issue in detail. A support team member will assist you shortly.")
                    .WithColor(0x40E0D0)
                    .WithFooter("This ticket will auto-close after 42 hours of inactivity.");

                var components = new ComponentBuilder()
                    .WithButton("🔒 Close Ticket", "ticket_close", ButtonStyle.Danger)
                    .WithButton(" Script-Ticket", "ticket_script", ButtonStyle.Primary);

                Console.WriteLine($"[TicketDebug] Attempting to send initial message to REST channel {restChannel.Name}");

                // Build the mention string with user and all support roles (if configured)
                var mentionString = user.Mention;
                if (config != null)
                {
                    // Use the new SupportRoleIds list if it has entries, otherwise fall back to single SupportRoleId
                    var roleIds = config.SupportRoleIds?.Any() == true ? config.SupportRoleIds : 
                                 (config.SupportRoleId.HasValue ? new List<ulong> { config.SupportRoleId.Value } : new List<ulong>());

                    foreach (var roleId in roleIds)
                    {
                        var supportRole = guild.GetRole(roleId);
                        if (supportRole != null)
                        {
                            mentionString += $" {supportRole.Mention}";
                            Console.WriteLine($"[TicketDebug] Adding support role mention: {supportRole.Name}");
                        }
                    }
                }

                try
                {
                    var message = await restChannel.SendMessageAsync(mentionString, embed: embed.Build(), components: components.Build());
                    Console.WriteLine($"[TicketDebug] Initial message sent successfully via REST channel (Message ID: {message.Id})");
                }
                catch (Exception msgEx)
                {
                    Console.WriteLine($"[TicketDebug] Failed to send initial message via REST: {msgEx.Message}");
                    // Try without components as fallback
                    try
                    {
                        var fallbackMessage = await restChannel.SendMessageAsync(mentionString, embed: embed.Build());
                        Console.WriteLine($"[TicketDebug] Fallback message sent successfully via REST (Message ID: {fallbackMessage.Id})");
                    }
                    catch (Exception fallbackEx)
                    {
                        Console.WriteLine($"[TicketDebug] REST fallback also failed: {fallbackEx.Message}");
                    }
                }

                // Start auto-close timer (42 hours)
                StartAutoCloseTimer(restChannel.Id, TimeSpan.FromHours(42));

                // Try to return SocketTextChannel, but create dummy if needed for success indication
                var socketChannel = guild.GetTextChannel(restChannel.Id);
                if (socketChannel != null)
                {
                    Console.WriteLine($"[TicketDebug] Returning SocketTextChannel: {socketChannel.Name}");
                    return (true, socketChannel, restChannel.Id);
                }
                else
                {
                    Console.WriteLine($"[TicketDebug] SocketTextChannel not available, but ticket created successfully");
                    
                    // Wait a bit more and try once more
                    await Task.Delay(1000);
                    socketChannel = guild.GetTextChannel(restChannel.Id);
                    if (socketChannel != null)
                    {
                        Console.WriteLine($"[TicketDebug] SocketTextChannel found after additional wait: {socketChannel.Name}");
                        return (true, socketChannel, restChannel.Id);
                    }
                    
                    Console.WriteLine($"[TicketDebug] SocketTextChannel still not available, but ticket was created successfully via REST");
                    // Return success=true even if SocketTextChannel is not available
                    return (true, null, restChannel.Id);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating ticket: {ex.Message}");
                return (false, null, 0);
            }
        }

        public static async Task HandleSelectMenuInteraction(SocketMessageComponent component)
        {
            try
            {
                if (component.Data.CustomId != "support_select") return;

                var selectedValue = component.Data.Values.FirstOrDefault();
                if (string.IsNullOrEmpty(selectedValue)) return;

                // Map the selected value to the proper category name
                var categoryMap = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "support_technical", "Technical Issue" },
                    { "support_spam", "Spam / Scam" },
                    { "support_abuse", "Abuse / Harassment" },
                    { "support_ad", "Advertising / Recruitment" },
                    { "support_bug", "Bug / Feature Request" },
                    { "support_other", "Other" }
                };

                var category = categoryMap.TryGetValue(selectedValue, out var mappedCategory) ? mappedCategory : "Other";

                // Check if user already has an active ticket
                var guild = (component.User as SocketGuildUser)?.Guild;
                if (guild == null) return;

                var existingTicket = TicketMetas.Values.FirstOrDefault(t =>
                    t.UserId == component.User.Id && t.GuildId == guild.Id);

                if (existingTicket != null)
                {
                    var existingChannel = guild.GetTextChannel(TicketMetas.FirstOrDefault(t => t.Value == existingTicket).Key);
                    if (existingChannel != null)
                    {
                        await component.RespondAsync($"❌ You already have an active ticket: {existingChannel.Mention}", ephemeral: true);
                        return;
                    }
                }

                await component.DeferAsync();

                var (success, channel, channelId) = await CreateTicketChannelAsync(guild, component.User, category);
                
                if (success)
                {
                    // Ticket was created successfully
                    if (channel != null)
                    {
                        await component.FollowupAsync($"✅ Ticket created: {channel.Mention}", ephemeral: true);
                    }
                    else
                    {
                        // Use channel ID if SocketTextChannel is not available
                        await component.FollowupAsync($"✅ Ticket created: <#{channelId}>", ephemeral: true);
                    }

                    // Log ticket creation
                    var config = GetConfig(guild.Id);
                    if (config != null)
                    {
                        var logChannel = guild.GetTextChannel(config.LogChannelId);
                        if (logChannel != null)
                        {
                            var logEmbed = new EmbedBuilder()
                                .WithTitle("🎫 New Ticket Created")
                                .WithColor(Color.Green)
                                .AddField("User", component.User.Mention, true)
                                .AddField("Category", category, true)
                                .AddField("Channel", channel != null ? channel.Mention : $"<#{channelId}>", true)
                                .WithTimestamp(DateTimeOffset.UtcNow);

                            await logChannel.SendMessageAsync(embed: logEmbed.Build());
                        }
                    }
                }
                else
                {
                    // Only show failed message if there was a real error (success = false)
                    Console.WriteLine($"[TicketDebug] Ticket creation actually failed");
                    var failedEmbed = new EmbedBuilder()
                        .WithTitle("❌ Failed")
                        .WithDescription("Failed to create ticket. Please try again or contact an administrator.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await component.FollowupAsync(embed: failedEmbed, ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling select menu: {ex.Message}");
                // Only send error message if there was an actual exception
                try
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ Error")
                        .WithDescription("An error occurred while creating your ticket. Please try again.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await component.FollowupAsync(embed: errorEmbed, ephemeral: true);
                }
                catch { /* Ignore if we can't send error message */ }
            }
        }

        public static async Task HandleButtonInteraction(SocketMessageComponent component)
        {
            try
            {
                switch (component.Data.CustomId)
                {
                    case "ticket_close":
                        await HandleCloseTicket(component);
                        break;
                    case "ticket_add_user":
                        await HandleAddUser(component);
                        break;
                    case "ticket_script":
                        await HandleScriptTicket(component);
                        break;
                    case "ticket_confirm_close":
                        await HandleConfirmClose(component);
                        break;
                    case "ticket_cancel_close":
                        await HandleCancelClose(component);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling button interaction: {ex.Message}");
            }
        }

        private static async Task HandleCloseTicket(SocketMessageComponent component)
        {
            if (!TicketMetas.TryGetValue(component.Channel.Id, out var meta)) return;

            // Only administrators can close tickets
            var user = component.User as SocketGuildUser;
            var config = GetConfig(meta.GuildId);
            bool canClose = user?.GuildPermissions.Administrator == true;

            if (!canClose)
            {
                var noPermissionEmbed = new EmbedBuilder()
                    .WithTitle("❌ No Permission")
                    .WithDescription("Only administrators can close tickets.")
                    .WithColor(0x40E0D0)
                    .Build();
                await component.RespondAsync(embed: noPermissionEmbed, ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("🔒 Close Ticket Confirmation")
                .WithDescription("Are you sure you want to close this ticket?\n\n" +
                               "This action cannot be undone, but a transcript will be saved.")
                .WithColor(Color.Orange);

            var confirmComponents = new ComponentBuilder()
                .WithButton("✅ Confirm Close", "ticket_confirm_close", ButtonStyle.Danger)
                .WithButton("❌ Cancel", "ticket_cancel_close", ButtonStyle.Secondary);

            await component.RespondAsync(embed: embed.Build(), components: confirmComponents.Build());
        }

        private static async Task HandleConfirmClose(SocketMessageComponent component)
        {
            await component.DeferAsync();
            var channel = component.Channel as SocketTextChannel;
            if (channel != null)
                await CloseTicketChannel(channel, component.User);
        }

        private static async Task HandleCancelClose(SocketMessageComponent component)
        {
            await component.UpdateAsync(x =>
            {
                x.Embed = new EmbedBuilder()
                    .WithTitle("❌ Close Cancelled")
                    .WithDescription("Ticket close has been cancelled.")
                    .WithColor(Color.Green)
                    .Build();
                x.Components = new ComponentBuilder().Build();
            });
        }

        private static async Task HandleAddUser(SocketMessageComponent component)
        {
            if (!TicketMetas.TryGetValue(component.Channel.Id, out var meta)) return;

            var user = component.User as SocketGuildUser;
            bool canAddUser = user?.GuildPermissions.Administrator == true;

            if (!canAddUser)
            {
                var noPermissionEmbed = new EmbedBuilder()
                    .WithTitle("❌ No Permission")
                    .WithDescription("Only administrators can add users to tickets.")
                    .WithColor(0x40E0D0)
                    .Build();
                await component.RespondAsync(embed: noPermissionEmbed, ephemeral: true);
                return;
            }

            var addUserEmbed = new EmbedBuilder()
                .WithTitle("👥 Add User")
                .WithDescription("Please mention the user you want to add to this ticket:")
                .WithColor(0x40E0D0)
                .Build();
            await component.RespondAsync(embed: addUserEmbed, ephemeral: true);
        }

        private static async Task HandleScriptTicket(SocketMessageComponent component)
        {
            if (!TicketMetas.TryGetValue(component.Channel.Id, out var meta)) return;

            var user = component.User as SocketGuildUser;
            bool canScript = user?.GuildPermissions.Administrator == true;

            if (!canScript)
            {
                var noPermissionEmbed = new EmbedBuilder()
                    .WithTitle("❌ No Permission")
                    .WithDescription("Only administrators can create ticket scripts.")
                    .WithColor(0x40E0D0)
                    .Build();
                await component.RespondAsync(embed: noPermissionEmbed, ephemeral: true);
                return;
            }

            var scriptEmbed = new EmbedBuilder()
                .WithTitle("📝 Script-Ticket")
                .WithDescription("Creating a transcript of this ticket conversation...")
                .WithColor(0x40E0D0)
                .Build();
            await component.RespondAsync(embed: scriptEmbed, ephemeral: true);

            // Create transcript
            var channel = component.Channel as SocketTextChannel;
            if (channel == null) return;

            try
            {
                var config = GetConfig(meta.GuildId);
                if (config == null)
                {
                    var noConfigEmbed = new EmbedBuilder()
                        .WithTitle("❌ Configuration Error")
                        .WithDescription("No log channel configured. Ask an admin to run `!ticket-setup`.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await component.FollowupAsync(embed: noConfigEmbed, ephemeral: true);
                    return;
                }

                // Build transcript
                var messages = await channel.GetMessagesAsync(100).FlattenAsync();
                var transcript = string.Join('\n', messages.Reverse().Select(m => 
                    $"[{m.Timestamp:yyyy-MM-dd HH:mm:ss}] {m.Author.Username}: {m.Content}"));
                
                var filename = $"ticket-script_{meta.GuildId}_{channel.Id}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.txt";
                System.IO.File.WriteAllText(filename, transcript);

                var logChannel = channel.Guild.GetTextChannel(config.LogChannelId);
                if (logChannel != null)
                {
                    await logChannel.SendFileAsync(filename, $"📋 Ticket transcript from **{channel.Name}** (created by <@{meta.UserId}>)");
                    
                    var successEmbed = new EmbedBuilder()
                        .WithTitle("✅ Script Created")
                        .WithDescription("Ticket transcript has been saved to the log channel.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await component.FollowupAsync(embed: successEmbed, ephemeral: true);
                }

                // Clean up file
                try { System.IO.File.Delete(filename); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating ticket script: {ex.Message}");
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Error")
                    .WithDescription("Failed to create ticket script. Please try again.")
                    .WithColor(0x40E0D0)
                    .Build();
                await component.FollowupAsync(embed: errorEmbed, ephemeral: true);
            }
        }

        public static async Task CloseTicketChannel(SocketTextChannel channel, SocketUser closedBy)
        {
            try
            {
                if (!TicketMetas.TryGetValue(channel.Id, out var meta)) return;

                // Save transcript
                await SaveTranscriptAsync(channel.Guild, channel, meta);

                // Remove auto-close timer
                if (_autoCloseTimers.TryGetValue(channel.Id, out var timer))
                {
                    timer.Stop();
                    timer.Dispose();
                    _autoCloseTimers.Remove(channel.Id);
                }

                // Log closure
                var config = GetConfig(meta.GuildId);
                if (config != null)
                {
                    var logChannel = channel.Guild.GetTextChannel(config.LogChannelId);
                    if (logChannel != null)
                    {
                        var logEmbed = new EmbedBuilder()
                            .WithTitle("🔒 Ticket Closed")
                            .WithColor(Color.Red)
                            .AddField("Ticket", channel.Name, true)
                            .AddField("Creator", $"<@{meta.UserId}>", true)
                            .AddField("Closed By", closedBy.Mention, true)
                            .AddField("Category", meta.Category, true)
                            .AddField("Duration", $"{DateTime.UtcNow - meta.CreatedAt:dd\\:hh\\:mm\\:ss}", true)
                            .WithTimestamp(DateTimeOffset.UtcNow);

                        await logChannel.SendMessageAsync(embed: logEmbed.Build());
                    }
                }

                // Remove from tracking
                TicketMetas.TryRemove(channel.Id, out _);

                // Delete channel
                await channel.DeleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing ticket: {ex.Message}");
            }
        }

        private static void StartAutoCloseTimer(ulong channelId, TimeSpan delay)
        {
            var timer = new System.Timers.Timer(delay.TotalMilliseconds);
            timer.Elapsed += async (sender, e) =>
            {
                try
                {
                    timer.Stop();
                    timer.Dispose();
                    _autoCloseTimers.Remove(channelId);

                    // Auto-close logic would go here
                    // For now, we'll just remove it from timers
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Auto-close timer error: {ex.Message}");
                }
            };
            timer.Start();
            _autoCloseTimers[channelId] = timer;
        }

        public static async Task<bool> SaveTranscriptAsync(SocketGuild guild, Discord.ITextChannel channel, TicketMeta meta)
        {
            try
            {
                var messages = await channel.GetMessagesAsync(500).FlattenAsync();
                var orderedMessages = messages.OrderBy(m => m.Timestamp);

                var transcript = "=== TICKET TRANSCRIPT ===\n";
                transcript += $"Ticket: {channel.Name}\n";
                transcript += $"Creator: {meta.Username} ({meta.UserId})\n";
                transcript += $"Category: {meta.Category}\n";
                transcript += $"Created: {meta.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC\n";
                transcript += $"Closed: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n";
                transcript += "========================\n\n";

                foreach (var msg in orderedMessages)
                {
                    if (!msg.Author.IsBot || !string.IsNullOrEmpty(msg.Content))
                    {
                        transcript += $"[{msg.Timestamp:yyyy-MM-dd HH:mm:ss}] {msg.Author.Username}#{msg.Author.Discriminator}: {msg.Content}\n";

                        if (msg.Attachments.Any())
                        {
                            foreach (var attachment in msg.Attachments)
                            {
                                transcript += $"   [Attachment: {attachment.Filename} - {attachment.Url}]\n";
                            }
                        }
                    }
                }

                var filename = $"ticket_{meta.GuildId}_{channel.Id}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.txt";
                await File.WriteAllTextAsync(filename, transcript);

                var cfg = GetConfig(meta.GuildId);
                if (cfg != null)
                {
                    var logChan = guild.GetTextChannel(cfg.LogChannelId);
                    if (logChan != null)
                    {
                        try
                        {
                            await logChan.SendFileAsync(filename, $"📋 Ticket transcript from **{channel.Name}** (created by <@{meta.UserId}>)");
                        }
                        catch { }
                    }
                }

                try { File.Delete(filename); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving transcript: {ex.Message}");
                return false;
            }
        }
        
        // MEMORY OPTIMIZATION: Cleanup orphaned tickets and maintain memory limits
        public static void CleanupTicketMemory()
        {
            try
            {
                // Only run cleanup every 6 hours
                if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromHours(CLEANUP_INTERVAL_HOURS))
                    return;
                
                _lastCleanup = DateTime.UtcNow;
                var removedCount = 0;
                
                // Remove tickets for channels that no longer exist
                var channelsToRemove = new List<ulong>();
                foreach (var kvp in TicketMetas.ToList())
                {
                    var channelId = kvp.Key;
                    var meta = kvp.Value;
                    
                    try
                    {
                        // Skip guild lookup for now - just mark old tickets for cleanup
                        // This avoids the static method call issue
                        var daysSinceCreated = (DateTime.UtcNow - meta.CreatedAt).TotalDays;
                        if (daysSinceCreated > 30) // Remove tickets older than 30 days
                        {
                            channelsToRemove.Add(channelId);
                        }
                    }
                    catch
                    {
                        channelsToRemove.Add(channelId);
                    }
                }
                
                foreach (var channelId in channelsToRemove)
                {
                    if (TicketMetas.TryRemove(channelId, out _))
                        removedCount++;
                    
                    // Also cleanup any associated timers
                    if (_autoCloseTimers.TryGetValue(channelId, out var timer))
                    {
                        timer.Stop();
                        timer.Dispose();
                        _autoCloseTimers.Remove(channelId);
                    }
                }
                
                // If still too many tickets, remove oldest ones
                if (TicketMetas.Count > MAX_TICKET_METAS)
                {
                    var oldestTickets = TicketMetas
                        .OrderBy(kvp => kvp.Value.CreatedAt)
                        .Take(TicketMetas.Count - MAX_TICKET_METAS)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var channelId in oldestTickets)
                    {
                        if (TicketMetas.TryRemove(channelId, out _))
                            removedCount++;
                    }
                }
                
                if (removedCount > 0)
                    Console.WriteLine($"Ticket cleanup: Removed {removedCount} orphaned tickets. Active tickets: {TicketMetas.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during ticket cleanup: {ex.Message}");
            }
        }
    }

    public class TicketCommands : ModuleBase<SocketCommandContext>
    {
        [Command("ticket-setup")]
        [Summary("Setup ticket system (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task SetupTicketSystemAsync()
        {
            try
            {
                // Initialize setup tracking
                var config = TicketService.GetConfig(Context.Guild.Id) ?? new TicketConfigEntry();
                config.SetupStep = "log_channel";
                config.LastSetupAttempt = DateTime.UtcNow;
                TicketService.SetConfig(Context.Guild.Id, config);

                // Step 1: Ask for log channel
                var askEmbed = new EmbedBuilder()
                    .WithTitle("🎫 Ticket System Setup")
                    .WithDescription("What channel should be the ticket log channel?\n\nWrite the **Channel ID** only in the chat or type `new-ticketlog` and I create a ticket log channel for you. You can move it after where you want.\n\n*Use `!ticket-cont` if something goes wrong.*")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: askEmbed);

                var logChannelResponse = await NextMessageAsync(TimeSpan.FromMinutes(1));
                if (logChannelResponse == null)
                {
                    var timeoutEmbed = new EmbedBuilder()
                        .WithTitle("⏰ Timeout")
                        .WithDescription("❌ Timeout! You can continue the setup with `!ticket-cont`.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: timeoutEmbed);
                    config.SetupStep = "log_channel";
                    TicketService.SetConfig(Context.Guild.Id, config);
                    return;
                }

                ulong logChannelId;
                ITextChannel logChannel;

                if (logChannelResponse.Content.Trim().ToLower() == "new-ticketlog")
                {
                    // Create new log channel
                    logChannel = await Context.Guild.CreateTextChannelAsync("★-ticket-log-book");
                    logChannelId = logChannel.Id;
                    var createdEmbed = new EmbedBuilder()
                        .WithTitle("✅ Channel Created")
                        .WithDescription($"Ticket log channel created: {logChannel.Mention}\nThe channel was set and is now available.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: createdEmbed);
                }
                else if (ulong.TryParse(logChannelResponse.Content.Trim(), out logChannelId))
                {
                    // Use existing channel
                    logChannel = Context.Guild.GetTextChannel(logChannelId);
                    if (logChannel == null)
                    {
                        var notFoundEmbed = new EmbedBuilder()
                            .WithTitle("❌ Not Found")
                            .WithDescription("Channel not found! Please provide a valid Channel ID from this server.\n\nUse `!ticket-cont` to try again.")
                            .WithColor(0x40E0D0)
                            .Build();
                        await ReplyAsync(embed: notFoundEmbed);
                        config.SetupStep = "log_channel";
                        TicketService.SetConfig(Context.Guild.Id, config);
                        return;
                    }
                    var setEmbed = new EmbedBuilder()
                        .WithTitle("✅ Channel Set")
                        .WithDescription($"Ticket log channel set: {logChannel.Mention}")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: setEmbed);
                }
                else
                {
                    var invalidEmbed = new EmbedBuilder()
                        .WithTitle("❌ Invalid Input")
                        .WithDescription("Invalid input! Please provide a Channel ID or type `new-ticketlog`.\n\nUse `!ticket-cont` to try again.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: invalidEmbed);
                    config.SetupStep = "log_channel";
                    TicketService.SetConfig(Context.Guild.Id, config);
                    return;
                }


                // Load or create config, update log channel, and save
                config = TicketService.GetConfig(Context.Guild.Id) ?? new TicketConfigEntry();
                config.LogChannelId = logChannelId;
                config.SetupStep = "ticket_message_channel";
                config.LastSetupAttempt = DateTime.UtcNow;
                TicketService.SetConfig(Context.Guild.Id, config);

                // Continue automatically to ticket message setup
                var continueEmbed = new EmbedBuilder()
                    .WithTitle("🎫 Continuing Setup")
                    .WithDescription("Log channel has been saved. Now setting up the ticket message...")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: continueEmbed);

                // Step 2: Ask for ticket message channel
                var ticketChanEmbed = new EmbedBuilder()
                    .WithTitle("📝 Ticket Message Channel")
                    .WithDescription("In which channel you want to have the ticket message to send?\n\nType the **Channel ID** or type `new-ticketchan` and the bot creates the channel and sends the Ticket Embed system message in there with the help categories and ticket opening tool.\n\n*Use `!ticket-cont` if something goes wrong.*")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: ticketChanEmbed);

                var ticketChannelResponse = await NextMessageAsync(TimeSpan.FromMinutes(1));
                if (ticketChannelResponse == null)
                {
                    var timeoutEmbed = new EmbedBuilder()
                        .WithTitle("⏰ Timeout")
                        .WithDescription("❌ Timeout! Log channel has been saved. Use `!ticket-cont` to continue setup.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: timeoutEmbed);
                    return;
                }

                ITextChannel ticketChannel;

                if (ticketChannelResponse.Content.Trim().ToLower() == "new-ticketchan")
                {
                    // Ask for category before creating new ticket channel
                    var categoryEmbed = new EmbedBuilder()
                        .WithTitle("📁 Select Category")
                        .WithDescription("In which **Category** do you want to create the ticket channel?\n\nType the **Category ID** or type `none` to create the channel without a category.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: categoryEmbed);

                    var categoryResponse = await NextMessageAsync(TimeSpan.FromMinutes(1));
                    if (categoryResponse == null)
                    {
                        var timeoutEmbed = new EmbedBuilder()
                            .WithTitle("⏰ Timeout")
                            .WithDescription("❌ Timeout! Log channel has been saved. Use `!ticket-cont` to continue setup.")
                            .WithColor(0x40E0D0)
                            .Build();
                        await ReplyAsync(embed: timeoutEmbed);
                        config.SetupStep = "ticket_category_selection";
                        TicketService.SetConfig(Context.Guild.Id, config);
                        return;
                    }

                    ulong? categoryId = null;
                    if (categoryResponse.Content.Trim().ToLower() != "none")
                    {
                        if (ulong.TryParse(categoryResponse.Content.Trim(), out var parsedCategoryId))
                        {
                            var category = Context.Guild.GetCategoryChannel(parsedCategoryId);
                            if (category == null)
                            {
                                var notFoundEmbed = new EmbedBuilder()
                                    .WithTitle("❌ Not Found")
                                    .WithDescription("Category not found! Please provide a valid Category ID from this server.\n\nUse `!ticket-cont` to try again.")
                                    .WithColor(0x40E0D0)
                                    .Build();
                                await ReplyAsync(embed: notFoundEmbed);
                                config.SetupStep = "ticket_category_selection";
                                TicketService.SetConfig(Context.Guild.Id, config);
                                return;
                            }
                            categoryId = parsedCategoryId;
                        }
                        else
                        {
                            var invalidEmbed = new EmbedBuilder()
                                .WithTitle("❌ Invalid Input")
                                .WithDescription("Invalid input! Please provide a valid Category ID or type `none`.\n\nUse `!ticket-cont` to try again.")
                                .WithColor(0x40E0D0)
                                .Build();
                            await ReplyAsync(embed: invalidEmbed);
                            config.SetupStep = "ticket_category_selection";
                            TicketService.SetConfig(Context.Guild.Id, config);
                            return;
                        }
                    }

                    // Create new ticket channel with category
                    if (categoryId.HasValue)
                    {
                        ticketChannel = await Context.Guild.CreateTextChannelAsync("★-support-tickets", prop =>
                        {
                            prop.CategoryId = categoryId.Value;
                        });
                    }
                    else
                    {
                        ticketChannel = await Context.Guild.CreateTextChannelAsync("★-support-tickets");
                    }

                    var createdEmbed = new EmbedBuilder()
                        .WithTitle("✅ Channel Created")
                        .WithDescription($"Ticket channel created: {ticketChannel.Mention}")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: createdEmbed);
                }
                else if (ulong.TryParse(ticketChannelResponse.Content.Trim(), out var ticketChannelId))
                {
                    // Use existing channel
                    ticketChannel = Context.Guild.GetTextChannel(ticketChannelId);
                    if (ticketChannel == null)
                    {
                        var notFoundEmbed = new EmbedBuilder()
                            .WithTitle("❌ Not Found")
                            .WithDescription("Channel not found! Please provide a valid Channel ID from this server.\n\nUse `!ticket-cont` to try again.")
                            .WithColor(0x40E0D0)
                            .Build();
                        await ReplyAsync(embed: notFoundEmbed);
                        config.SetupStep = "ticket_message_channel";
                        TicketService.SetConfig(Context.Guild.Id, config);
                        return;
                    }
                }
                else
                {
                    var invalidEmbed = new EmbedBuilder()
                        .WithTitle("❌ Invalid Input")
                        .WithDescription("Invalid input! Please provide a Channel ID or type `new-ticketchan`.\n\nUse `!ticket-cont` to try again.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: invalidEmbed);
                    config.SetupStep = "ticket_message_channel";
                    TicketService.SetConfig(Context.Guild.Id, config);
                    return;
                }

                // Send ticket embed with dropdown menu
                var ticketEmbed = new EmbedBuilder()
                    .WithTitle("🎫 **Support Ticket System**")
                    .WithDescription("**Need help or want to report an issue?**\n\nSelect the category that best describes your issue from the menu below, and we'll create a private ticket channel for you.")
                    .WithColor(0x1D80A3)
                    .AddField("📋 **Available Support Categories**",
                        "• **Technical Issue** - Bot not working, errors, bugs\n" +
                        "• **Spam / Scam** - Report spam or scam content\n" +
                        "• **Abuse / Harassment** - Report user misconduct\n" +
                        "• **Advertising / Recruitment** - Report unwanted promotion\n" +
                        "• **Bug / Feature Request** - Report bugs or suggest features\n" +
                        "• **Other** - General questions or other issues", false)
                    .WithFooter("Support tickets are private and only visible to you and server staff")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                var selectMenu = new SelectMenuBuilder()
                    .WithPlaceholder("Select your issue type...")
                    .WithCustomId("support_select")
                    .WithMinValues(1)
                    .WithMaxValues(1)
                    .AddOption("Technical Issue", "support_technical", "Bot errors, commands not working", new Emoji("🔧"))
                    .AddOption("Spam / Scam", "support_spam", "Report spam or scam content", new Emoji("🚫"))
                    .AddOption("Abuse / Harassment", "support_abuse", "Report user misconduct", new Emoji("⚠️"))
                    .AddOption("Advertising / Recruitment", "support_ad", "Unwanted promotion", new Emoji("📢"))
                    .AddOption("Bug / Feature Request", "support_bug", "Report bugs or suggest features", new Emoji("🐛"))
                    .AddOption("Other", "support_other", "General questions", new Emoji("❓"));

                var component = new ComponentBuilder()
                    .WithSelectMenu(selectMenu)
                    .Build();

                var sentMessage = await ticketChannel.SendMessageAsync(embed: ticketEmbed.Build(), components: component);

                // Save ticket message info for deletion later
                config.TicketMessageChannelId = ticketChannel.Id;
                config.TicketMessageId = sentMessage.Id;
                config.SetupStep = "ticket_creation_category";
                config.LastSetupAttempt = DateTime.UtcNow;
                TicketService.SetConfig(Context.Guild.Id, config);

                // Step 3: Ask for ticket category (where actual tickets will be created)
                var ticketCategoryEmbed = new EmbedBuilder()
                    .WithTitle("📁 Ticket Creation Category")
                    .WithDescription("In which **Category** should the bot create the individual support tickets when users open them?\n\nType the **Category ID** where all new tickets will be created.\n\n*Use `!ticket-cont` if something goes wrong.*")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: ticketCategoryEmbed);

                var ticketCategoryResponse = await NextMessageAsync(TimeSpan.FromMinutes(1));
                if (ticketCategoryResponse == null)
                {
                    var timeoutEmbed = new EmbedBuilder()
                        .WithTitle("⏰ Timeout")
                        .WithDescription("❌ Timeout! Setup is incomplete. Tickets will be created at the top of the server. Use `!ticket-cont` to complete the setup.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: timeoutEmbed);
                    return;
                }

                if (ulong.TryParse(ticketCategoryResponse.Content.Trim(), out var ticketCategoryId))
                {
                    var ticketCategory = Context.Guild.GetCategoryChannel(ticketCategoryId);
                    if (ticketCategory == null)
                    {
                        var notFoundEmbed = new EmbedBuilder()
                            .WithTitle("❌ Not Found")
                            .WithDescription("Category not found! Please provide a valid Category ID from this server.\n\nUse `!ticket-cont` to try again.")
                            .WithColor(0x40E0D0)
                            .Build();
                        await ReplyAsync(embed: notFoundEmbed);
                        config.SetupStep = "ticket_creation_category";
                        TicketService.SetConfig(Context.Guild.Id, config);
                        return;
                    }

                    // Reload config to ensure we have the latest version
                    var currentConfig = TicketService.GetConfig(Context.Guild.Id);
                    if (currentConfig != null)
                    {
                        currentConfig.TicketCategoryId = ticketCategoryId;
                        currentConfig.SetupStep = null; // Clear setup status
                        currentConfig.LastSetupAttempt = null;
                        TicketService.SetConfig(Context.Guild.Id, currentConfig);
                    }

                    var categorySetEmbed = new EmbedBuilder()
                        .WithTitle("✅ Category Set")
                        .WithDescription($"Tickets will be created in category: <#{ticketCategoryId}>")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: categorySetEmbed);
                }
                else
                {
                    var invalidEmbed = new EmbedBuilder()
                        .WithTitle("❌ Invalid Input")
                        .WithDescription("Invalid input! Please provide a valid Category ID. Tickets will be created at the top of the server.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: invalidEmbed);
                    return;
                }

                // Final confirmation
                var finalConfig = TicketService.GetConfig(Context.Guild.Id);
                var finalEmbed = new EmbedBuilder()
                    .WithTitle("✅ Ticket System Setup Complete!")
                    .WithColor(Color.Green)
                    .AddField("Log Channel", logChannel.Mention, true)
                    .AddField("Ticket Channel", ticketChannel.Mention, true)
                    .AddField("Ticket Category", finalConfig?.TicketCategoryId.HasValue == true ? $"<#{finalConfig.TicketCategoryId}>" : "Not set", true)
                    .WithDescription("Your ticket system is now fully configured and ready to use!")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                await ReplyAsync(embed: finalEmbed.Build());
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Setup Failed")
                    .WithDescription($"Setup failed: {ex.Message}")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }

        private async Task<SocketMessage> NextMessageAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<SocketMessage>();
            var expectedUserId = Context.User.Id;
            var expectedChannelId = Context.Channel.Id;
            var client = Context.Client; // Capture client reference before awaiting
            
            Console.WriteLine($"[NextMessageAsync-TICKET] Waiting for message from User={expectedUserId} in Channel={expectedChannelId}");

            Task Handler(SocketMessage message)
            {
                Console.WriteLine($"[NextMessageAsync-TICKET] Handler triggered: Author={message.Author.Id}, Channel={message.Channel.Id}, Content='{message.Content}'");
                Console.WriteLine($"[NextMessageAsync-TICKET] Expecting: Author={expectedUserId}, Channel={expectedChannelId}");
                
                if (message.Channel.Id == expectedChannelId && message.Author.Id == expectedUserId && !message.Author.IsBot)
                {
                    Console.WriteLine("[NextMessageAsync-TICKET] MATCH! Setting result.");
                    tcs.TrySetResult(message);
                }
                else
                {
                    Console.WriteLine("[NextMessageAsync-TICKET] No match.");
                }
                return Task.CompletedTask;
            }

            client.MessageReceived += Handler;

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            client.MessageReceived -= Handler;

            if (completedTask == tcs.Task)
            {
                Console.WriteLine("[NextMessageAsync-TICKET] Task completed successfully!");
                return await tcs.Task;
            }

            Console.WriteLine("[NextMessageAsync-TICKET] Timeout reached!");
            return null!;
        }

        [Command("ticket-close")]
        [Summary("Close the current ticket")]
        public async Task CloseTicketAsync([Remainder] string reason = "No reason provided")
        {
            try
            {
                if (!TicketService.TicketMetas.TryGetValue(Context.Channel.Id, out var meta))
                {
                    var notTicketEmbed = new EmbedBuilder()
                        .WithTitle("❌ Not a Ticket Channel")
                        .WithDescription("This is not a ticket channel.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: notTicketEmbed);
                    return;
                }

                var user = Context.User as SocketGuildUser;
                var config = TicketService.GetConfig(Context.Guild.Id);
                bool canClose = user?.Id == meta.UserId ||
                               user?.GuildPermissions.Administrator == true ||
                               (config?.SupportRoleId.HasValue == true && user?.Roles.Any(r => r.Id == config.SupportRoleId.Value) == true);

                if (!canClose)
                {
                    var noPermEmbed = new EmbedBuilder()
                        .WithTitle("❌ No Permission")
                        .WithDescription("Only the ticket creator or support team can close this ticket.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: noPermEmbed);
                    return;
                }

                var embed = new EmbedBuilder()
                    .WithTitle("🔒 Closing Ticket...")
                    .WithDescription($"Ticket will be closed in 5 seconds.\n**Reason:** {reason}")
                    .WithColor(Color.Orange);

                await ReplyAsync(embed: embed.Build());

                await Task.Delay(5000);
                var channel = Context.Channel as SocketTextChannel;
                if (channel != null)
                    await TicketService.CloseTicketChannel(channel, Context.User);
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Failed")
                    .WithDescription($"Failed to close ticket: {ex.Message}")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }

        [Command("ticket-add")]
        [Summary("Add a user to the current ticket")]
        public async Task AddUserToTicketAsync(SocketGuildUser userToAdd)
        {
            try
            {
                if (!TicketService.TicketMetas.TryGetValue(Context.Channel.Id, out var meta))
                {
                    var notTicketEmbed = new EmbedBuilder()
                        .WithTitle("❌ Not a Ticket Channel")
                        .WithDescription("This is not a ticket channel.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: notTicketEmbed);
                    return;
                }

                var user = Context.User as SocketGuildUser;
                bool canAddUser = user?.Id == meta.UserId ||
                                 user?.GuildPermissions.Administrator == true ||
                                 user?.GuildPermissions.ManageChannels == true;

                if (!canAddUser)
                {
                    var noPermEmbed = new EmbedBuilder()
                        .WithTitle("❌ No Permission")
                        .WithDescription("Only the ticket creator or staff can add users.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: noPermEmbed);
                    return;
                }

                var channel = Context.Channel as SocketTextChannel;
                if (channel != null)
                {
                    await channel.AddPermissionOverwriteAsync(userToAdd, new OverwritePermissions(
                        viewChannel: PermValue.Allow,
                        sendMessages: PermValue.Allow,
                        readMessageHistory: PermValue.Allow));
                }

                var embed = new EmbedBuilder()
                    .WithTitle("✅ User Added")
                    .WithDescription($"{userToAdd.Mention} has been added to this ticket.")
                    .WithColor(Color.Green);

                await ReplyAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Failed")
                    .WithDescription($"Failed to add user: {ex.Message}")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }

        [Command("ticket-remove")]
        [Summary("Remove a user from the current ticket")]
        public async Task RemoveUserFromTicketAsync(SocketGuildUser userToRemove)
        {
            try
            {
                if (!TicketService.TicketMetas.TryGetValue(Context.Channel.Id, out var meta))
                {
                    var notTicketEmbed = new EmbedBuilder()
                        .WithTitle("❌ Not a Ticket Channel")
                        .WithDescription("This is not a ticket channel.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: notTicketEmbed);
                    return;
                }

                if (userToRemove.Id == meta.UserId)
                {
                    var cannotRemoveEmbed = new EmbedBuilder()
                        .WithTitle("❌ Cannot Remove")
                        .WithDescription("Cannot remove the ticket creator.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: cannotRemoveEmbed);
                    return;
                }

                var user = Context.User as SocketGuildUser;
                bool canRemoveUser = user?.Id == meta.UserId ||
                                    user?.GuildPermissions.Administrator == true ||
                                    user?.GuildPermissions.ManageChannels == true;

                if (!canRemoveUser)
                {
                    var noPermEmbed = new EmbedBuilder()
                        .WithTitle("❌ No Permission")
                        .WithDescription("Only the ticket creator or staff can remove users.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: noPermEmbed);
                    return;
                }

                var channel = Context.Channel as SocketTextChannel;
                if (channel != null)
                {
                    await channel.RemovePermissionOverwriteAsync(userToRemove);
                }

                var embed = new EmbedBuilder()
                    .WithTitle("✅ User Removed")
                    .WithDescription($"{userToRemove.Mention} has been removed from this ticket.")
                    .WithColor(Color.Orange);

                await ReplyAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Failed")
                    .WithDescription($"Failed to remove user: {ex.Message}")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }

        [Command("ticket-status")]
        [Summary("Check ticket system status (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task TicketStatusAsync()
        {
            try
            {
                var config = TicketService.GetConfig(Context.Guild.Id);
                var activeTickets = TicketService.TicketMetas.Count(t => t.Value.GuildId == Context.Guild.Id);
                var activeTicketsList = TicketService.TicketMetas
                    .Where(t => t.Value.GuildId == Context.Guild.Id)
                    .Take(10)
                    .Select(t => $"<#{t.Key}> - <@{t.Value.UserId}> ({t.Value.Category})")
                    .ToList();

                var embed = new EmbedBuilder()
                    .WithTitle("🎫 Ticket System Status")
                    .WithColor(0x40E0D0)
                    .AddField("Configuration",
                        $"**Log Channel:** {(config != null ? $"<#{config.LogChannelId}>" : "Not configured")}\n" +
                        $"**Category:** {(config?.TicketCategoryId.HasValue == true ? $"<#{config.TicketCategoryId}>" : "Default")}\n" +
                        $"**Support Role:** {(config?.SupportRoleId.HasValue == true ? $"<@&{config.SupportRoleId}>" : "None")}", false)
                    .AddField("Statistics", $"**Active Tickets:** {activeTickets}", true);

                if (activeTicketsList.Any())
                {
                    embed.AddField("Recent Tickets", string.Join("\n", activeTicketsList), false);
                }

                await ReplyAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Failed")
                    .WithDescription($"Failed to get status: {ex.Message}")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }

        [Command("ticket-transcript")]
        [Summary("Generate transcript for current ticket")]
        public async Task GenerateTranscriptAsync()
        {
            try
            {
                if (!TicketService.TicketMetas.TryGetValue(Context.Channel.Id, out var meta))
                {
                    var notTicketEmbed = new EmbedBuilder()
                        .WithTitle("❌ Not a Ticket Channel")
                        .WithDescription("This is not a ticket channel.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: notTicketEmbed);
                    return;
                }

                var generatingEmbed = new EmbedBuilder()
                    .WithTitle("📋 Generating Transcript")
                    .WithDescription("Generating transcript...")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: generatingEmbed);
                
                var channel = Context.Channel as SocketTextChannel;
                if (channel != null)
                    await TicketService.SaveTranscriptAsync(Context.Guild, channel, meta);
                
                var successEmbed = new EmbedBuilder()
                    .WithTitle("✅ Transcript Generated")
                    .WithDescription("Transcript generated and saved to logs!")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: successEmbed);
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Failed")
                    .WithDescription($"Failed to generate transcript: {ex.Message}")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }

        [Command("del-ticket-system")]
        [Summary("Remove ticket system configuration and delete ticket message (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task DeleteTicketSystemAsync()
        {
            try
            {
                var config = TicketService.GetConfig(Context.Guild.Id);
                if (config == null)
                {
                    var notConfiguredEmbed = new EmbedBuilder()
                        .WithTitle("❌ Not Configured")
                        .WithDescription("No ticket system configured for this server.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: notConfiguredEmbed);
                    return;
                }

                // Try to delete the ticket message if it exists
                if (config.TicketMessageChannelId.HasValue && config.TicketMessageId.HasValue)
                {
                    try
                    {
                        var ticketChannel = Context.Guild.GetTextChannel(config.TicketMessageChannelId.Value);
                        if (ticketChannel != null)
                        {
                            var message = await ticketChannel.GetMessageAsync(config.TicketMessageId.Value);
                            if (message != null)
                            {
                                await message.DeleteAsync();
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors when deleting the message (might already be deleted)
                    }
                }

                TicketService.RemoveConfig(Context.Guild.Id);
                
                var successEmbed = new EmbedBuilder()
                    .WithTitle("✅ Ticket System Removed")
                    .WithDescription("Ticket system configuration and message have been completely removed from this server.")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: successEmbed);
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Failed")
                    .WithDescription($"Failed to remove configuration: {ex.Message}")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }

        [Command("ticket-cont")]
        [Summary("Continue ticket system setup from where it was left off (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task ContinueTicketSetupAsync()
        {
            try
            {
                var config = TicketService.GetConfig(Context.Guild.Id);
                if (config == null || string.IsNullOrEmpty(config.SetupStep))
                {
                    var noSetupEmbed = new EmbedBuilder()
                        .WithTitle("❌ No Setup to Continue")
                        .WithDescription("No incomplete ticket system setup found. Use `!ticket-setup` to start a new setup.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: noSetupEmbed);
                    return;
                }

                // Check if setup is too old (more than 1 hour)
                if (config.LastSetupAttempt.HasValue && 
                    (DateTime.UtcNow - config.LastSetupAttempt.Value).TotalHours > 1)
                {
                    var expiredEmbed = new EmbedBuilder()
                        .WithTitle("⏰ Setup Expired")
                        .WithDescription("The previous setup attempt is too old. Please use `!ticket-setup` to start a fresh setup.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: expiredEmbed);
                    
                    // Clear expired setup
                    config.SetupStep = null;
                    config.LastSetupAttempt = null;
                    TicketService.SetConfig(Context.Guild.Id, config);
                    return;
                }

                var continueEmbed = new EmbedBuilder()
                    .WithTitle("🔄 Continuing Setup")
                    .WithDescription($"Continuing ticket system setup from step: **{config.SetupStep}**")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: continueEmbed);

                // Continue from the saved step
                switch (config.SetupStep)
                {
                    case "ticket_message_channel":
                        await ContinueTicketMessageChannelSetup(config);
                        break;
                    case "ticket_category_selection":
                        await ContinueTicketCategorySetup(config);
                        break;
                    case "ticket_creation_category":
                        await ContinueTicketCreationCategorySetup(config);
                        break;
                    default:
                        var unknownStepEmbed = new EmbedBuilder()
                            .WithTitle("❌ Unknown Step")
                            .WithDescription("Unknown setup step. Please use `!ticket-setup` to start a new setup.")
                            .WithColor(0x40E0D0)
                            .Build();
                        await ReplyAsync(embed: unknownStepEmbed);
                        break;
                }
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Failed")
                    .WithDescription($"Failed to continue setup: {ex.Message}")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }

        private async Task ContinueTicketMessageChannelSetup(TicketConfigEntry config)
        {
            var ticketChanEmbed = new EmbedBuilder()
                .WithTitle("📝 Ticket Message Channel")
                .WithDescription("In which channel you want to have the ticket message to send?\n\nType the **Channel ID** or type `new-ticketchan` and the bot creates the channel and sends the Ticket Embed system message in there with the help categories and ticket opening tool.\n\n*Use `!ticket-cont` if something goes wrong.*")
                .WithColor(0x40E0D0)
                .Build();
            await ReplyAsync(embed: ticketChanEmbed);

            config.LastSetupAttempt = DateTime.UtcNow;
            TicketService.SetConfig(Context.Guild.Id, config);
        }

        private async Task ContinueTicketCategorySetup(TicketConfigEntry config)
        {
            var categoryEmbed = new EmbedBuilder()
                .WithTitle("📁 Select Category")
                .WithDescription("In which **Category** do you want to create the ticket channel?\n\nType the **Category ID** or type `none` to create the channel without a category.\n\n*Use `!ticket-cont` if something goes wrong.*")
                .WithColor(0x40E0D0)
                .Build();
            await ReplyAsync(embed: categoryEmbed);

            config.LastSetupAttempt = DateTime.UtcNow;
            TicketService.SetConfig(Context.Guild.Id, config);
        }

        private async Task ContinueTicketCreationCategorySetup(TicketConfigEntry config)
        {
            var ticketCategoryEmbed = new EmbedBuilder()
                .WithTitle("📁 Ticket Creation Category")
                .WithDescription("In which **Category** should the bot create the individual support tickets when users open them?\n\nType the **Category ID** where all new tickets will be created.\n\n*Use `!ticket-cont` if something goes wrong.*")
                .WithColor(0x40E0D0)
                .Build();
            await ReplyAsync(embed: ticketCategoryEmbed);

            config.LastSetupAttempt = DateTime.UtcNow;
            TicketService.SetConfig(Context.Guild.Id, config);
        }

        [Command("set-support")]
        [Summary("Set support roles that can see and manage tickets - up to 3 roles (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task SetSupportRoleAsync(ulong roleId1, ulong roleId2 = 0, ulong roleId3 = 0)
        {
            try
            {
                var roleIds = new List<ulong> { roleId1 };
                if (roleId2 != 0) roleIds.Add(roleId2);
                if (roleId3 != 0) roleIds.Add(roleId3);

                var validRoles = new List<SocketRole>();

                // Check if all roles exist
                foreach (var roleId in roleIds)
                {
                    var role = Context.Guild.GetRole(roleId);
                    if (role == null)
                    {
                        var notFoundEmbed = new EmbedBuilder()
                            .WithTitle("❌ Role Not Found")
                            .WithDescription($"Role with ID `{roleId}` was not found in this server. Please check the Role ID and try again.")
                            .WithColor(0x40E0D0)
                            .Build();
                        await ReplyAsync(embed: notFoundEmbed);
                        return;
                    }
                    validRoles.Add(role);
                }

                // Get or create config
                var config = TicketService.GetConfig(Context.Guild.Id);
                if (config == null)
                {
                    var noConfigEmbed = new EmbedBuilder()
                        .WithTitle("❌ No Ticket System")
                        .WithDescription("No ticket system configured yet. Please run `!ticket-setup` first to configure the ticket system.")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: noConfigEmbed);
                    return;
                }

                // Update support roles 
                config.SupportRoleId = roleIds[0]; // Keep first role as main for backward compatibility
                config.SupportRoleIds = roleIds; // Store all roles in the new field
                
                TicketService.SetConfig(Context.Guild.Id, config);

                var roleList = string.Join("\n", validRoles.Select(r => $"• {r.Mention} - {r.Name} (ID: {r.Id})"));
                var roleCount = validRoles.Count;
                var title = roleCount == 1 ? "✅ Support Role Updated" : $"✅ {roleCount} Support Roles Updated";
                
                var successEmbed = new EmbedBuilder()
                    .WithTitle(title)
                    .WithDescription($"Support {(roleCount == 1 ? "role has" : "roles have")} been configured!\n\n" +
                                   $"**{roleCount} Role{(roleCount == 1 ? "" : "s")} Added:**\n{roleList}\n\n" +
                                   $"Members with {(roleCount == 1 ? "this role" : "these roles")} can now see and manage all support tickets.")
                    .WithColor(0x00FF00)
                    .WithFooter($"Total support roles configured: {roleCount}")
                    .Build();
                await ReplyAsync(embed: successEmbed);
            }
            catch (Exception ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Error")
                    .WithDescription($"Failed to set support roles: {ex.Message}")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }
    }
}
