using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using Discord;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MainbotCSharp.Modules
{
    public class VoiceTemplate
    {
        public string Name { get; set; } = "Voice";
        public int Limit { get; set; } = 0;
    }

    public class VoiceConfig
    {
        public ulong? JoinToCreateChannel { get; set; }
        public ulong? JoinToCreateCategory { get; set; }
        public ulong? VoiceChannelCategory { get; set; }
        public ulong? VoiceLogChannel { get; set; }
        public ulong? AllowedRole { get; set; }
        public Dictionary<ulong, ActiveChannelInfo> ActiveChannels { get; set; } = new();
        public Dictionary<string, VoiceTemplate> Templates { get; set; } = new() { ["custom"] = new VoiceTemplate() };
    }

    public class ActiveChannelInfo 
    { 
        public ulong OwnerId { get; set; }
        public long CreatedAt { get; set; }
        public string Template { get; set; } = "custom";
        public bool IsPrivate { get; set; } = false;
    }

    public class VoiceLogEntry
    {
        public ulong UserId { get; set; }
        public string Username { get; set; } = "";
        public string Action { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public long Timestamp { get; set; }
    }

    public class VoiceUserStats
    {
        public string Username { get; set; } = "";
        public int TotalJoins { get; set; }
        public int ChannelsCreated { get; set; }
        public long TotalTimeSpent { get; set; }
        public long LastActivity { get; set; }
    }

    public class VoiceLogs
    {
        public List<VoiceLogEntry> Logs { get; set; } = new();
        public Dictionary<ulong, VoiceUserStats> Stats { get; set; } = new();
        
        // MEMORY OPTIMIZATION: Cleanup methods
        public void CleanupOldLogs(int maxLogs = 1000)
        {
            if (Logs.Count > maxLogs)
            {
                // Keep only the newest logs
                Logs = Logs.OrderByDescending(log => log.Timestamp).Take(maxLogs).ToList();
            }
        }
        
        public void CleanupInactiveUsers(int maxUsers = 1000, int inactiveDays = 30) // Reduced from 5000 to 1000 for memory optimization
        {
            if (Stats.Count > maxUsers)
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-inactiveDays).ToUnixTimeSeconds();
                var inactiveUsers = Stats
                    .Where(kvp => kvp.Value.LastActivity < cutoff)
                    .OrderBy(kvp => kvp.Value.LastActivity)
                    .Take(Stats.Count - maxUsers)
                    .Select(kvp => kvp.Key)
                    .ToList();
                    
                foreach (var userId in inactiveUsers)
                    Stats.Remove(userId);
            }
        }
    }

    public static class VoiceService
    {
        private const string VOICE_CONFIG_FILE = "voice_config.json";
        private const string VOICE_LOGS_FILE = "voice_logs.json";
        private static Dictionary<ulong, VoiceConfig> _configs = LoadVoiceConfigs();
        private static VoiceLogs _logs = LoadVoiceLogs();

        // Get Voice Config für spezifischen Guild
        public static VoiceConfig GetConfig(ulong guildId)
        {
            if (!_configs.TryGetValue(guildId, out var config))
            {
                config = new VoiceConfig();
                _configs[guildId] = config;
                SaveVoiceConfigs();
            }
            return config;
        }

        // Set Voice Config für spezifischen Guild
        public static void SetConfig(ulong guildId, VoiceConfig config)
        {
            _configs[guildId] = config;
            SaveVoiceConfigs();
        }

        // Remove Voice Config für spezifischen Guild
        public static void RemoveConfig(ulong guildId)
        {
            if (_configs.ContainsKey(guildId))
            {
                _configs.Remove(guildId);
                SaveVoiceConfigs();
            }
        }

        private static Dictionary<ulong, VoiceConfig> LoadVoiceConfigs()
        {
            try
            {
                if (!File.Exists(VOICE_CONFIG_FILE)) return new Dictionary<ulong, VoiceConfig>();
                var txt = File.ReadAllText(VOICE_CONFIG_FILE);
                return JsonSerializer.Deserialize<Dictionary<ulong, VoiceConfig>>(txt) ?? new Dictionary<ulong, VoiceConfig>();
            }
            catch { return new Dictionary<ulong, VoiceConfig>(); }
        }

        private static void SaveVoiceConfigs()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_configs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(VOICE_CONFIG_FILE, txt);
            }
            catch { }
        }

        private static VoiceLogs LoadVoiceLogs()
        {
            try
            {
                if (!File.Exists(VOICE_LOGS_FILE)) return new VoiceLogs();
                var txt = File.ReadAllText(VOICE_LOGS_FILE);
                return JsonSerializer.Deserialize<VoiceLogs>(txt) ?? new VoiceLogs();
            }
            catch { return new VoiceLogs(); }
        }

        private static void SaveVoiceLogs()
        {
            try
            {
                // MEMORY OPTIMIZATION: Cleanup before saving
                _logs.CleanupOldLogs();
                _logs.CleanupInactiveUsers();
                
                var txt = JsonSerializer.Serialize(_logs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(VOICE_LOGS_FILE, txt);
            }
            catch (Exception ex) { Console.WriteLine("Failed to save voice logs: " + ex); }
        }

        public static void SetVoiceLogChannel(ulong guildId, ulong channelId)
        {
            var config = GetConfig(guildId);
            config.VoiceLogChannel = channelId;
            SetConfig(guildId, config);
        }

        public static void SetVoiceCategory(ulong guildId, ulong categoryId)
        {
            var config = GetConfig(guildId);
            config.VoiceChannelCategory = categoryId;
            SetConfig(guildId, config);
        }

        public static void SetAllowedRole(ulong guildId, ulong roleId)
        {
            var config = GetConfig(guildId);
            config.AllowedRole = roleId;
            SetConfig(guildId, config);
        }

        public static void SetJoinToCreateChannel(ulong guildId, ulong channelId, ulong categoryId)
        {
            var config = GetConfig(guildId);
            config.JoinToCreateChannel = channelId;
            config.JoinToCreateCategory = categoryId;
            SetConfig(guildId, config);
        }

        public static async Task HandleVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
        {
            try
            {
                var guild = (oldState.VoiceChannel ?? newState.VoiceChannel)?.Guild;
                if (guild == null) return;

                var member = guild.GetUser(user.Id);
                if (member == null) return;

                // User joined a channel
                if (oldState.VoiceChannel == null && newState.VoiceChannel != null)
                {
                    await HandleUserJoined(member, newState.VoiceChannel);
                }
                // User left a channel
                else if (oldState.VoiceChannel != null && newState.VoiceChannel == null)
                {
                    await HandleUserLeft(member, oldState.VoiceChannel);
                }
                // User moved between channels
                else if (oldState.VoiceChannel != null && newState.VoiceChannel != null && oldState.VoiceChannel.Id != newState.VoiceChannel.Id)
                {
                    await HandleUserLeft(member, oldState.VoiceChannel);
                    await HandleUserJoined(member, newState.VoiceChannel);
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[VoiceService] Error in HandleVoiceStateUpdated: {ex.Message}"); 
            }
        }

        private static async Task HandleUserJoined(SocketGuildUser member, SocketVoiceChannel channel)
        {
            var cfg = GetConfig(member.Guild.Id);
            if (cfg?.JoinToCreateChannel == null || channel.Id != cfg.JoinToCreateChannel.Value) return;

            Console.WriteLine($"[VoiceService] {member.Username} joined Join-to-Create channel");

            // Check role permission
            if (cfg.AllowedRole.HasValue)
            {
                var allowedRole = member.Guild.GetRole(cfg.AllowedRole.Value);
                if (allowedRole != null && !member.Roles.Contains(allowedRole))
                {
                    Console.WriteLine($"[VoiceService] {member.Username} lacks required role");
                    return;
                }
            }

            await CreateVoiceChannel(member, channel.Guild, cfg);
        }

        private static async Task HandleUserLeft(SocketGuildUser member, SocketVoiceChannel channel)
        {
            var cfg = GetConfig(member.Guild.Id);
            if (cfg?.ActiveChannels == null || !cfg.ActiveChannels.ContainsKey(channel.Id)) return;

            Console.WriteLine($"[VoiceService] {member.Username} left system channel: {channel.Name}");

            // Log the voice activity
            await LogVoiceActivity(member, "Left", channel.Name);

            // Delete channel if empty (with retry mechanism)
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        await Task.Delay(100 + (i * 100)); // 100ms, 200ms, 300ms, 400ms, 500ms delays
                        
                        var guild = channel.Guild;
                        var currentChannel = guild.GetVoiceChannel(channel.Id);
                        var currentConfig = GetConfig(guild.Id); // Get fresh config each time
                        
                        if (currentChannel == null)
                        {
                            currentConfig.ActiveChannels.Remove(channel.Id);
                            SetConfig(guild.Id, currentConfig);
                            return;
                        }
                        
                        // Count only real users (not bots, not deafened/muted in weird states)
                        var realUsers = currentChannel.Users.Where(u => !u.IsBot && u.VoiceChannel?.Id == currentChannel.Id).ToList();
                        
                        if (realUsers.Count == 0)
                        {
                            Console.WriteLine($"[VoiceService] Deleting empty channel: {currentChannel.Name}");
                            await currentChannel.DeleteAsync(new RequestOptions { AuditLogReason = "Voice channel empty" });
                            
                            currentConfig.ActiveChannels.Remove(channel.Id);
                            SetConfig(guild.Id, currentConfig);
                            
                            Console.WriteLine($"[VoiceService] Successfully deleted: {channel.Name}");
                            return;
                        }
                        else
                        {
                            Console.WriteLine($"[VoiceService] Channel {currentChannel.Name} still has {realUsers.Count} real users (total: {currentChannel.Users.Count})");
                            foreach (var u in currentChannel.Users)
                            {
                                Console.WriteLine($"  - {u.Username} (Bot: {u.IsBot}, VoiceChannel: {u.VoiceChannel?.Name ?? "null"})");
                            }
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[VoiceService] Deletion attempt {i + 1} failed: {ex.Message}");
                    }
                }
            });
        }

        private static async Task CreateVoiceChannel(SocketGuildUser member, SocketGuild guild, VoiceConfig cfg)
        {
            try
            {
                var template = cfg.Templates.ContainsKey("custom") ? cfg.Templates["custom"] : cfg.Templates.Values.First();
                var channelName = $"{template.Name} - {member.Username}";
                var categoryId = cfg.VoiceChannelCategory ?? cfg.JoinToCreateCategory;

                Console.WriteLine($"[VoiceService] Creating channel: {channelName}");

                var newChannel = await guild.CreateVoiceChannelAsync(channelName, prop =>
                {
                    if (categoryId.HasValue) prop.CategoryId = categoryId.Value;
                    prop.UserLimit = template.Limit;
                    prop.PermissionOverwrites = new[] {
                        new Overwrite(member.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Allow, speak: PermValue.Allow))
                    };
                });

                // Move user to new channel
                try 
                { 
                    await member.ModifyAsync(properties => properties.ChannelId = newChannel.Id);
                    Console.WriteLine($"[VoiceService] Moved {member.Username} to {newChannel.Name}");
                } 
                catch (Exception moveEx) 
                { 
                    Console.WriteLine($"[VoiceService] Failed to move user: {moveEx.Message}");
                }

                // Add to active channels
                cfg.ActiveChannels[newChannel.Id] = new ActiveChannelInfo 
                { 
                    OwnerId = member.Id, 
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Template = "custom"
                };
                SetConfig(guild.Id, cfg);

                Console.WriteLine($"[VoiceService] Successfully created channel for {member.Username}");

                // Log the voice activity
                await LogVoiceActivity(member, "Created Channel", newChannel.Name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceService] Error creating voice channel: {ex.Message}");
            }
        }

        private static async Task LogVoiceActivity(SocketGuildUser member, string action, string channelName)
        {
            try
            {
                var config = GetConfig(member.Guild.Id);
                if (config.VoiceLogChannel == null) return;

                var guild = member.Guild;
                var logChannel = guild.GetTextChannel(config.VoiceLogChannel.Value);
                if (logChannel == null) return;

                // Create log entry
                var logEntry = new VoiceLogEntry
                {
                    UserId = member.Id,
                    Username = member.Username,
                    Action = action,
                    ChannelName = channelName,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                // Save to logs
                _logs.Logs.Add(logEntry);
                SaveVoiceLogs();

                // Send to Discord channel
                var embed = new EmbedBuilder()
                    .WithAuthor(member.Username, member.GetAvatarUrl() ?? member.GetDefaultAvatarUrl())
                    .WithDescription($"**{action}** `{channelName}`")
                    .WithColor(action.Contains("Left") ? Color.Red : Color.Green)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await logChannel.SendMessageAsync(embed: embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceService] Error logging voice activity: {ex.Message}");
            }
        }

        public static async Task StartBackgroundTasks(DiscordSocketClient client)
        {
            // Background tasks can be added here if needed
            await Task.CompletedTask;
        }

        // Public methods for commands - DEPRECATED: Use GetConfig(guildId) instead
        public static ActiveChannelInfo? GetActiveChannelByOwner(ulong guildId, ulong ownerId) => GetConfig(guildId).ActiveChannels.Values.FirstOrDefault(a => a.OwnerId == ownerId);
        public static VoiceLogs GetLogs() => _logs;
        public static void RemoveActiveChannel(ulong guildId, ulong channelId) 
        { 
            var config = GetConfig(guildId);
            if (config.ActiveChannels.ContainsKey(channelId)) 
            { 
                config.ActiveChannels.Remove(channelId); 
                SetConfig(guildId, config);
            } 
        }
    }

    public class VoiceCommands : ModuleBase<SocketCommandContext>
    {
        [Command("voicesetup")]
        [Summary("Setup voice system with log channel, category and role (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task VoiceSetupAsync(ulong logChannelId, ulong voiceCategoryId, ulong roleId)
        {
            try
            {
                // Validate inputs
                var logChannel = Context.Guild.GetTextChannel(logChannelId);
                var voiceCategory = Context.Guild.GetCategoryChannel(voiceCategoryId);
                var allowedRole = Context.Guild.GetRole(roleId);

                if (logChannel == null)
                {
                    await ReplyAsync("❌ Invalid log channel ID");
                    return;
                }
                if (voiceCategory == null)
                {
                    await ReplyAsync("❌ Invalid voice category ID");
                    return;
                }
                if (allowedRole == null)
                {
                    await ReplyAsync("❌ Invalid role ID");
                    return;
                }

                // Update configuration
                VoiceService.SetVoiceLogChannel(Context.Guild.Id, logChannelId);
                VoiceService.SetVoiceCategory(Context.Guild.Id, voiceCategoryId);
                VoiceService.SetAllowedRole(Context.Guild.Id, roleId);

                var embed = new EmbedBuilder()
                    .WithTitle("✅ Voice System Configured")
                    .WithColor(Color.Green)
                    .AddField("Log Channel", logChannel.Mention, true)
                    .AddField("Voice Category", voiceCategory.Name, true)
                    .AddField("Allowed Role", allowedRole.Mention, true)
                    .WithDescription("Use `!create [category-id]` to create the Join-to-Create channel")
                    .Build();
                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Setup failed: {ex.Message}");
            }
        }

        [Command("create")]
        [Summary("Create Join-to-Create voice channel (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task CreateJoinToCreateAsync(ulong categoryId)
        {
            try
            {
                var category = Context.Guild.GetCategoryChannel(categoryId);
                if (category == null)
                {
                    await ReplyAsync("❌ Invalid category ID");
                    return;
                }

                var channel = await Context.Guild.CreateVoiceChannelAsync("➕ Create Voice", properties =>
                {
                    properties.CategoryId = categoryId;
                    properties.UserLimit = 0;
                });

                VoiceService.SetJoinToCreateChannel(Context.Guild.Id, channel.Id, categoryId);

                var embed = new EmbedBuilder()
                    .WithTitle("✅ Join-to-Create Channel Created")
                    .WithColor(Color.Green)
                    .AddField("Channel", channel.Mention, true)
                    .AddField("Category", category.Name, true)
                    .WithDescription("Voice system is now active! Users can join this channel to create their own voice room.")
                    .Build();
                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Creation failed: {ex.Message}");
            }
        }

        [Command("voice-cleanup")]
        [Summary("Cleanup all empty voice channels (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task VoiceCleanupAsync()
        {
            try
            {
                var config = VoiceService.GetConfig(Context.Guild.Id);
                if (config?.VoiceChannelCategory == null)
                {
                    await ReplyAsync("❌ Voice system not configured. Use `!voicesetup` first.");
                    return;
                }

                var category = Context.Guild.GetCategoryChannel(config.VoiceChannelCategory.Value);
                if (category == null)
                {
                    await ReplyAsync("❌ Voice category not found.");
                    return;
                }

                int deletedCount = 0;
                var channels = category.Channels.OfType<SocketVoiceChannel>().ToList();
                
                foreach (var voiceChannel in channels)
                {
                    // Skip the Join-to-Create channel
                    if (config.JoinToCreateChannel.HasValue && voiceChannel.Id == config.JoinToCreateChannel.Value)
                        continue;

                    // Check if channel is empty
                    if (voiceChannel.Users.Count == 0)
                    {
                        try
                        {
                            await voiceChannel.DeleteAsync(new RequestOptions { AuditLogReason = "Manual voice cleanup - empty channel" });
                            deletedCount++;
                            
                            // Remove from active channels if exists
                            if (config.ActiveChannels.ContainsKey(voiceChannel.Id))
                            {
                                VoiceService.RemoveActiveChannel(Context.Guild.Id, voiceChannel.Id);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[VoiceCleanup] Failed to delete {voiceChannel.Name}: {ex.Message}");
                        }
                    }
                }

                await ReplyAsync($"🧹 Cleaned up **{deletedCount}** empty voice channels.");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Cleanup failed: {ex.Message}");
            }
        }

        [Command("voicename")]
        [Summary("Rename your voice channel")]
        public async Task VoiceNameAsync([Remainder] string name)
        {
            try
            {
                // Check if user is in a voice channel
                var userChannel = (Context.User as SocketGuildUser)?.VoiceChannel;
                if (userChannel == null)
                {
                    await ReplyAsync("❌ You must be in a voice channel to rename it.");
                    return;
                }

                // Check if this is an active voice channel (created by our system)
                var config = VoiceService.GetConfig(Context.Guild.Id);
                if (!config.ActiveChannels.ContainsKey(userChannel.Id))
                {
                    await ReplyAsync("❌ You can only rename voice channels created by the voice system.");
                    return;
                }

                await userChannel.ModifyAsync(p => p.Name = name);
                await ReplyAsync($"✅ Voice channel renamed to **{name}**");
                
                Console.WriteLine($"[VoiceService] {Context.User.Username} renamed channel to: {name}");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to rename channel: {ex.Message}");
                Console.WriteLine($"[VoiceService] Rename error: {ex.Message}");
            }
        }

        [Command("voicelimit")]
        [Summary("Set user limit for your voice channel (0-99, 0=unlimited)")]
        public async Task VoiceLimitAsync(int limit)
        {
            try
            {
                if (limit < 0 || limit > 99)
                {
                    await ReplyAsync("❌ User limit must be between 0 and 99 (0 = unlimited).");
                    return;
                }

                // Check if user is in a voice channel
                var userChannel = (Context.User as SocketGuildUser)?.VoiceChannel;
                if (userChannel == null)
                {
                    await ReplyAsync("❌ You must be in a voice channel to set its limit.");
                    return;
                }

                // Check if this is an active voice channel (created by our system)
                var config = VoiceService.GetConfig(Context.Guild.Id);
                if (!config.ActiveChannels.ContainsKey(userChannel.Id))
                {
                    await ReplyAsync("❌ You can only set limits on voice channels created by the voice system.");
                    return;
                }

                await userChannel.ModifyAsync(p => p.UserLimit = limit);
                
                var limitText = limit == 0 ? "unlimited" : limit.ToString();
                await ReplyAsync($"✅ Voice channel user limit set to **{limitText}**");
                
                Console.WriteLine($"[VoiceService] {Context.User.Username} set channel limit to: {limit}");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to set channel limit: {ex.Message}");
                Console.WriteLine($"[VoiceService] Limit error: {ex.Message}");
            }
        }

        [Command("voicelock")]
        [Summary("Lock your voice channel (make it private)")]
        public async Task VoiceLockAsync()
        {
            try
            {
                // Check if user is in a voice channel
                var userChannel = (Context.User as SocketGuildUser)?.VoiceChannel;
                if (userChannel == null)
                {
                    await ReplyAsync("❌ You must be in a voice channel to lock it.");
                    return;
                }

                // Check if this is an active voice channel (created by our system)
                var config = VoiceService.GetConfig(Context.Guild.Id);
                if (!config.ActiveChannels.ContainsKey(userChannel.Id))
                {
                    await ReplyAsync("❌ You can only lock voice channels created by the voice system.");
                    return;
                }

                // Get @everyone role to deny connect permission
                var everyoneRole = Context.Guild.EveryoneRole;
                await userChannel.AddPermissionOverwriteAsync(everyoneRole, new OverwritePermissions(connect: PermValue.Deny));

                await ReplyAsync("🔒 Voice channel locked! Only current members can stay.");
                Console.WriteLine($"[VoiceService] {Context.User.Username} locked channel: {userChannel.Name}");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to lock channel: {ex.Message}");
                Console.WriteLine($"[VoiceService] Lock error: {ex.Message}");
            }
        }

        [Command("voiceunlock")]
        [Summary("Unlock your voice channel (make it public)")]
        public async Task VoiceUnlockAsync()
        {
            try
            {
                // Check if user is in a voice channel
                var userChannel = (Context.User as SocketGuildUser)?.VoiceChannel;
                if (userChannel == null)
                {
                    await ReplyAsync("❌ You must be in a voice channel to unlock it.");
                    return;
                }

                // Check if this is an active voice channel (created by our system)
                var config = VoiceService.GetConfig(Context.Guild.Id);
                if (!config.ActiveChannels.ContainsKey(userChannel.Id))
                {
                    await ReplyAsync("❌ You can only unlock voice channels created by the voice system.");
                    return;
                }

                // Remove @everyone permission override to allow everyone to connect
                var everyoneRole = Context.Guild.EveryoneRole;
                await userChannel.RemovePermissionOverwriteAsync(everyoneRole);

                await ReplyAsync("🔓 Voice channel unlocked! Everyone can join now.");
                Console.WriteLine($"[VoiceService] {Context.User.Username} unlocked channel: {userChannel.Name}");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to unlock channel: {ex.Message}");
                Console.WriteLine($"[VoiceService] Unlock error: {ex.Message}");
            }
        }

        [Command("remove-voice-setup")]
        [Summary("Remove voice system configuration (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task RemoveVoiceSetupAsync()
        {
            VoiceService.RemoveConfig(Context.Guild.Id);
            await ReplyAsync("✅ Voice system disabled. Existing channels will remain but no new ones will be created.");
        }
        [Command("voicehelp")]
        [Summary("Show voice system help")]
        public async Task VoiceHelpAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("🎤 Voice System Help")
                .WithColor(Color.Purple)
                .AddField("Setup (Admin)",
                    "`!voicesetup [log-channel-id] [category-id] [role-id]` - Configure system\n" +
                    "`!create [category-id]` - Create Join-to-Create channel\n" +
                    "`!voice-cleanup` - Clean up empty channels\n" +
                    "`!remove-voice-setup` - Disable voice system", false)
                .AddField("User Commands",
                    "`!voicename <name>` - Rename your channel\n" +
                    "`!voicelimit <0-99>` - Set user limit (0=unlimited)\n" +
                    "`!voicelock` - Make your channel private\n" +
                    "`!voiceunlock` - Make your channel public", false)
                .WithDescription("Join the ➕ Create Voice channel to automatically create your own voice room!")
                .Build();
            await ReplyAsync(embed: embed);
        }
    }
}