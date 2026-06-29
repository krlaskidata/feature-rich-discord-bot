using System.Reflection;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;
using MainbotCSharp.Modules;
using MainbotCSharp.Services;

namespace MainbotCSharp
{
    public class Bot
    {
        // Scoped permissions instead of Administrator (permissions=8)
        private const string BOT_INVITE_LINK = "https://discord.com/oauth2/authorize?client_id=1435244593301159978&permissions=1099812957271&integration_type=0&scope=bot";
        private static readonly GuildPermission[] RequiredGuildPermissions =
        {
            GuildPermission.ViewChannel,
            GuildPermission.SendMessages,
            GuildPermission.EmbedLinks,
            GuildPermission.AttachFiles,
            GuildPermission.ReadMessageHistory,
            GuildPermission.AddReactions,
            GuildPermission.ManageMessages,
            GuildPermission.ManageChannels,
            GuildPermission.ManageRoles,
            GuildPermission.CreateInstantInvite,
            GuildPermission.ModerateMembers,
            GuildPermission.Connect,
            GuildPermission.Speak,
            GuildPermission.MoveMembers,
            GuildPermission.MuteMembers,
            GuildPermission.DeafenMembers
        };
        
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;

        public Bot()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent | GatewayIntents.GuildVoiceStates | GatewayIntents.GuildMembers
            };
            _client = new DiscordSocketClient(config);
            
            var commandConfig = new CommandServiceConfig
            {
                DefaultRunMode = RunMode.Async
            };
            _commands = new CommandService(commandConfig);

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .AddSingleton<XPService>(provider => new XPService(provider.GetRequiredService<DiscordSocketClient>()))
                .AddSingleton<GiveawayService>()
                .BuildServiceProvider();

            _client.Log += Client_Log;
            _commands.Log += Commands_Log;
            _client.Ready += Client_Ready;
            _client.MessageReceived += HandleMessageAsync;
            _client.InteractionCreated += InteractionCreatedAsync;
            _client.UserVoiceStateUpdated += MainbotCSharp.Modules.VoiceService.HandleVoiceStateUpdatedAsync;
            _client.UserVoiceStateUpdated += async (user, before, after) => 
            {
                var xpService = _services.GetService<XPService>();
                if (xpService != null)
                    await xpService.HandleVoiceUpdate(user, before, after);
            };
            _client.UserJoined += async (user) =>
            {
                try
                {
                    await MainbotCSharp.Modules.SecurityService.HandleUserJoinedAsync(user);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Security HandleUserJoined error: {ex.Message}");
                }
            };
            _client.JoinedGuild += OnJoinedGuild;
        }

        public async Task InitializeAsync()
        {
            var token = Environment.GetEnvironmentVariable("MAINBOT_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Missing required environment variable MAINBOT_TOKEN.");
            }

            if (!ValidateConfiguration())
            {
                throw new InvalidOperationException("Invalid bot configuration. Check MAINBOT_OWNERS and MAINBOT_ALLOWED_GUILDS.");
            }

            await _commands.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
        }

        private async Task Client_Ready()
        {
            Console.WriteLine($"Mainbot ready. Logged in as {_client.CurrentUser}");
            await _client.SetActivityAsync(new Game("Managing the server"));

            await CheckAndLeaveUnauthorizedServers();
            LogMissingGuildPermissions();

            try
            {
                var xpService = _services.GetService<XPService>();
                if (xpService != null)
                    await xpService.RestoreVoiceSessions(_client.Guilds);
            }
            catch (Exception ex) { Console.WriteLine("RestoreVoiceSessions error: " + ex); }

            try { await MainbotCSharp.Modules.VoiceService.StartBackgroundTasks(_client); } catch (Exception ex) { Console.WriteLine("VoiceService start error: " + ex); }

            try
            {
                MainbotCSharp.Modules.BirthdayService.Initialize(_client);
            }
            catch (Exception ex)
            {
                Console.WriteLine("BirthdayService initialization error: " + ex);
            }

            try
            {
                BumpReminderService.Initialize(_client);
            }
            catch (Exception ex)
            {
                Console.WriteLine("BumpReminderService initialization error: " + ex);
            }

            try
            {
                MainbotCSharp.Modules.SecurityService.RestoreCleanupTimers(_client);
            }
            catch (Exception ex)
            {
                Console.WriteLine("SecurityService RestoreCleanupTimers error: " + ex);
            }

            // PingService is now handled by separate C-Ping bot
        }

        private void LogMissingGuildPermissions()
        {
            foreach (var guild in _client.Guilds)
            {
                var currentUser = guild.CurrentUser;
                if (currentUser == null)
                {
                    Console.WriteLine($"[PERMS] {guild.Name} ({guild.Id}): Could not resolve bot guild user.");
                    continue;
                }

                var missing = RequiredGuildPermissions
                    .Where(permission => !currentUser.GuildPermissions.Has(permission))
                    .ToList();

                if (missing.Count == 0)
                {
                    Console.WriteLine($"[PERMS] {guild.Name} ({guild.Id}): OK (all required guild permissions present).");
                    continue;
                }

                Console.WriteLine(
                    $"[PERMS] {guild.Name} ({guild.Id}): Missing guild permissions -> {string.Join(", ", missing)}");
            }
        }

        private Task Client_Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private Task Commands_Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private async Task HandleMessageAsync(SocketMessage rawMessage)
        {
            if (!await MessageProtectionService.TryEnterMessagePipelineAsync())
            {
                Console.WriteLine("[THROTTLE] Message dropped due to overload.");
                return;
            }

            try
            {
                Console.WriteLine($"[DEBUG] Message received: AuthorId={rawMessage.Author.Id}, ChannelId={rawMessage.Channel.Id}, ContentLength={rawMessage.Content?.Length ?? 0}, Attachments={rawMessage.Attachments.Count}, IsBot={rawMessage.Author.IsBot}");
            
                try
                {
                    BumpReminderService.HandleDisboardMessage(rawMessage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("BumpReminderService HandleDisboardMessage error: " + ex);
                }

            // PingService is now handled by separate C-Ping bot

                try
                {
                    await MainbotCSharp.Modules.SecurityService.HandleMessageAsync(rawMessage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SecurityService HandleMessageAsync error: {ex.Message}");
                    return;
                }

                if (!(rawMessage is SocketUserMessage message)) return;
                if (message.Author.IsBot) return;

                try
                {
                    var xpService = _services.GetService<XPService>();
                    if (xpService != null)
                        await xpService.HandleMessage(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("XPService HandleMessage error: " + ex);
                }

                int argPos = 0;
                if (!(message.HasCharPrefix('!', ref argPos) || message.HasMentionPrefix(_client.CurrentUser, ref argPos)))
                {
                    Console.WriteLine($"[DEBUG] Message without command prefix: AuthorId={message.Author.Id}, ChannelId={message.Channel.Id}, ContentLength={message.Content?.Length ?? 0}");
                    return;
                }

                if (MessageProtectionService.IsCommandRateLimited(message.Author.Id))
                {
                    Console.WriteLine($"[THROTTLE] Command rate limit hit: AuthorId={message.Author.Id}, ChannelId={message.Channel.Id}");
                    return;
                }

                var commandText = message.Content.Substring(argPos).Trim();
                var commandName = commandText.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()
                    ?.ToLower();

                // Commands that EVERYONE can use (not just authorized users)
                var allowedForEveryone = new[] {
                    // Birthday System
                    "birthdayset", "mybirthdayinfo", "birthdayremove", "birthdaylist",
                    // XP System
                    "xp", "checkxp", "leaderboard", "top",
                    // Economy System
                    "work", "daily", "balance", "bal", "profile", "rank",
                    "coinflip", "dice", "slots", "blackjack",
                    "deposit", "dep", "withdraw", "with", "rob", "pay",
                    // Help Commands
                    "help", "economy-help", "eco-help", "money-help", "mungahelpdesk",
                    // Info Commands
                    "info", "ping", "vs-noctaly",
                    // Fun Commands
                    "gm", "gn", "hi",
                    // Security System (per-server, guild admins can configure)
                    "setsecuritymod", "disable", "status",
                    "security-filters", "security-rules", "sfilters",
                    "close-sticket",
                    "cleanup-now",
                    "cleanup-intervall", "setcleanupinterval",
                    "delcleanup-intervall", "cleanupdel", "removecleanupinterval",
                    "give-love",
                    // Verify System (per-server, admins & staff can use)
                    "verify-setup", "verify", "unverify", "verify-status"
                };

                var isGuildAdmin = message.Author is SocketGuildUser guildMember && guildMember.GuildPermissions.Administrator;

                if (!AuthorizedUsers.Contains(message.Author.Id) && !allowedForEveryone.Contains(commandName) && !isGuildAdmin)
                {
                    Console.WriteLine($"[AUTH] Unauthorized command attempt: AuthorId={message.Author.Id}, ChannelId={message.Channel.Id}, Command={commandName ?? "<unknown>"}");
                    return;
                }

                if (message.Content.ToLower().StartsWith("!invite") && IsBotOwner(message.Author.Id))
                {
                    await HandleInviteCommand(message);
                    return;
                }

                if (message.Content.ToLower().StartsWith("!server-invites") && IsBotOwner(message.Author.Id))
                {
                    await HandleServerInvitesCommand(message);
                    return;
                }

                var context = new SocketCommandContext(_client, message);

                await _commands.ExecuteAsync(context, argPos, _services);
            }
            finally
            {
                MessageProtectionService.ExitMessagePipeline();
            }
        }

        private async Task InteractionCreatedAsync(SocketInteraction interaction)
        {
            try
            {
                if (interaction.Type == Discord.InteractionType.MessageComponent)
                {
                    var comp = interaction as SocketMessageComponent;
                    if (comp == null) return;
                    var customId = comp.Data.CustomId;

                    if (customId == "helpdesk_select")
                    {
                        var value = comp.Data.Values.FirstOrDefault();
                        string replyText = "";
                        switch (value)
                        {
                            case "help_all":
                                replyText = "Full command list: run `!help` in your server channel to see all commands.";
                                break;
                            case "help_voice":
                                replyText = "Voice help: run `!helpvoice` in your server channel for voice commands.";
                                break;
                            case "help_secure":
                                replyText = "Security help: run `!helpsecure` to see moderation commands.";
                                break;
                            case "help_bump":
                                replyText = "Bump help: run `!helpbump` for bump/reminder commands.";
                                break;
                            case "help_birth":
                                replyText = "Birthday help: run `!helpbirth` to configure birthdays.";
                                break;
                            default:
                                replyText = "No information available for this selection.";
                                break;
                        }
                        await comp.RespondAsync(replyText, ephemeral: true);
                        return;
                    }

                    if (customId == "support_select")
                    {
                        await TicketService.HandleSelectMenuInteraction(comp);
                        return;
                    }

                    if (customId.StartsWith("ticket_"))
                    {
                        await TicketService.HandleButtonInteraction(comp);
                        return;
                    }

                    if (customId == "verify_button")
                    {
                        await VerifyService.HandleVerifyButtonAsync(comp);
                        return;
                    }

                    if (customId.StartsWith("security_appeal|", StringComparison.Ordinal))
                    {
                        await MainbotCSharp.Modules.SecurityService.HandleSecurityAppealInteractionAsync(comp);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                try { await interaction.RespondAsync("An error occurred while handling the interaction.", ephemeral: true); } catch { }
                Console.WriteLine("Interaction handler error: " + ex);
            }
        }

        private async Task HandleInviteCommand(SocketUserMessage message)
        {
            try
            {
                if (MainBotInvites.Count == 0)
                {
                    await message.Channel.SendMessageAsync("No server invites configured. Set MAINBOT_INVITES in environment variables.");
                    return;
                }

                await message.Channel.SendMessageAsync("🤖 **Main Bot Auto-Join Starting...**\n" +
                    "I will now attempt to join all my designated servers!");

                int successCount = 0;
                int totalCount = MainBotInvites.Count;

                foreach (var invite in MainBotInvites)
                {
                    try
                    {
                        await message.Channel.SendMessageAsync($"🔄 Attempting to join server via invite: `{invite.Value}`");
                        
                        var inviteInfo = await _client.GetInviteAsync(invite.Value);
                        
                        if (inviteInfo != null)
                        {
                            var existingGuild = _client.Guilds.FirstOrDefault(g => g.Id == inviteInfo.GuildId);
                            if (existingGuild != null)
                            {
                                await message.Channel.SendMessageAsync($"✅ Already in server: **{existingGuild.Name}**");
                                successCount++;
                            }
                            else
                            {
                                await message.Channel.SendMessageAsync($"⚠️ Server found: **{inviteInfo.GuildName}** but I need to be invited manually.\n" +
                                    $"Please use this bot invite link: {BOT_INVITE_LINK}");
                            }
                        }
                        
                        await Task.Delay(2000);
                    }
                    catch (Exception ex)
                    {
                        await message.Channel.SendMessageAsync($"❌ Failed to process invite `{invite.Value}`: {ex.Message}");
                    }
                }

                await message.Channel.SendMessageAsync($"🎯 **Auto-join complete!**\n" +
                    $"Successfully processed {successCount}/{totalCount} servers.\n" +
                    $"I am now in {_client.Guilds.Count} total servers and will never leave the authorized ones!");
            }
            catch (Exception ex)
            {
                await message.Channel.SendMessageAsync($"❌ Error during invite process: {ex.Message}");
            }
        }

        private async Task HandleServerInvitesCommand(SocketUserMessage message)
        {
            try
            {
                if (!EnableServerInviteExport)
                {
                    await message.Channel.SendMessageAsync("⛔ `!server-invites` is disabled. Set MAINBOT_ENABLE_SERVER_INVITES_EXPORT=true to enable it.");
                    return;
                }

                if (!message.Content.Trim().Equals("!server-invites confirm", StringComparison.OrdinalIgnoreCase))
                {
                    await message.Channel.SendMessageAsync("⚠️ Confirmation required. Run `!server-invites confirm`.");
                    return;
                }

                var lines = new System.Text.StringBuilder();
                lines.AppendLine("**INVITES FOR ALLOWED SERVERS ONLY**");

                foreach (var guild in _client.Guilds)
                {
                    if (!AllowedGuilds.Contains(guild.Id))
                    {
                        continue;
                    }

                    try
                    {
                        var channel = guild.TextChannels
                            .Where(c => c.GetPermissionOverwrite(guild.EveryoneRole)?.SendMessages != PermValue.Deny)
                            .OrderBy(c => c.Position)
                            .FirstOrDefault();

                        if (channel == null)
                        {
                            lines.AppendLine($"- {guild.Name} — no accessible channel");
                            continue;
                        }

                        var invite = await channel.CreateInviteAsync(maxAge: 600, maxUses: 1, isUnique: true);
                        lines.AppendLine($"- {guild.Name} — https://discord.gg/{invite.Code}");
                    }
                    catch
                    {
                        lines.AppendLine($"- {guild.Name} — could not create invite");
                    }
                }

                await message.Author.SendMessageAsync(lines.ToString());
            }
            catch (Exception ex)
            {
                await message.Channel.SendMessageAsync($"❌ Error: {ex.Message}");
            }
        }

        private static readonly ulong[] AuthorizedUsers = ParseUlongListFromEnvironment("MAINBOT_AUTHORIZED_USERS");

        private static readonly ulong[] BotOwners = ParseUlongListFromEnvironment("MAINBOT_OWNERS");

        public static bool IsBotOwner(ulong userId)
        {
            return BotOwners.Contains(userId);
        }

        private static readonly ulong[] AllowedGuilds = ParseUlongListFromEnvironment("MAINBOT_ALLOWED_GUILDS");

        private static readonly bool EnableServerInviteExport = ParseBoolFromEnvironment("MAINBOT_ENABLE_SERVER_INVITES_EXPORT");

        private static readonly Dictionary<string, string> MainBotInvites = ParseInviteMapFromEnvironment("MAINBOT_INVITES");

        private static bool ValidateConfiguration()
        {
            bool isValid = true;

            if (BotOwners.Length == 0)
            {
                Console.WriteLine("ERROR: MAINBOT_OWNERS is empty. Bot owner security checks cannot work.");
                isValid = false;
            }

            if (AllowedGuilds.Length == 0)
            {
                Console.WriteLine("ERROR: MAINBOT_ALLOWED_GUILDS is empty. Guild whitelist protection cannot work.");
                isValid = false;
            }

            if (AuthorizedUsers.Length == 0)
            {
                Console.WriteLine("WARNING: MAINBOT_AUTHORIZED_USERS is empty. Only allowlisted commands and guild admins can use protected commands.");
            }

            if (MainBotInvites.Count == 0)
            {
                Console.WriteLine("WARNING: MAINBOT_INVITES is empty. !invite auto-join helper will do nothing.");
            }

            return isValid;
        }

        private static ulong[] ParseUlongListFromEnvironment(string variableName)
        {
            var raw = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<ulong>();
            }

            var values = new List<ulong>();
            var tokens = raw.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (ulong.TryParse(token.Trim(), out var parsed))
                {
                    values.Add(parsed);
                }
                else
                {
                    Console.WriteLine($"WARNING: Invalid numeric value '{token}' in {variableName}.");
                }
            }

            return values.Distinct().ToArray();
        }

        private static Dictionary<string, string> ParseInviteMapFromEnvironment(string variableName)
        {
            var raw = Environment.GetEnvironmentVariable(variableName);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return result;
            }

            var entries = raw.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            int fallbackIndex = 1;

            foreach (var entry in entries)
            {
                var trimmed = entry.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var code = parts[1].Trim();
                    if (key.Length == 0 || code.Length == 0)
                    {
                        Console.WriteLine($"WARNING: Invalid invite entry '{trimmed}' in {variableName}. Use name=code.");
                        continue;
                    }

                    result[key] = code;
                    continue;
                }

                result[$"invite{fallbackIndex}"] = trimmed;
                fallbackIndex++;
            }

            return result;
        }

        private static bool ParseBoolFromEnvironment(string variableName)
        {
            var raw = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return string.Equals(raw.Trim(), "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(raw.Trim(), "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(raw.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        }

        private async Task OnJoinedGuild(SocketGuild guild)
        {
            Console.WriteLine($"🎯 Bot was added to server: {guild.Name} (ID: {guild.Id})");
            
            bool serverWhitelisted = AllowedGuilds.Contains(guild.Id);
            
            Console.WriteLine($"🔍 Server whitelist check: {guild.Name} (ID: {guild.Id}) - Whitelisted: {serverWhitelisted}");
            
            if (!serverWhitelisted)
            {
                Console.WriteLine($"🚫 Leaving unauthorized server: {guild.Name} (ID: {guild.Id})");
                
                try
                {
                    var systemChannel = guild.SystemChannel ?? guild.DefaultChannel ?? guild.TextChannels.FirstOrDefault();
                    if (systemChannel != null)
                    {
                        var embed = new EmbedBuilder()
                            .WithTitle("🚫 Private Bot")
                            .WithDescription("Dieser Server gehört deiner damaligen Freundin du Hund. Kickst du sie, kickst du mich auch.")
                            .WithColor(Color.Red)
                            .WithCurrentTimestamp()
                            .Build();
                        
                        await systemChannel.SendMessageAsync(embed: embed);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not send leave message: {ex.Message}");
                }
                
                await Task.Delay(3000);
                await guild.LeaveAsync();
            }
            else
            {
                Console.WriteLine($"✅ Staying in authorized server: {guild.Name} (ID: {guild.Id})");
            }
        }

        private async Task CheckAndLeaveUnauthorizedServers()
        {
            Console.WriteLine("🔍 Checking current servers against whitelist...");
            
            foreach (var guild in _client.Guilds.ToList())
            {
                bool serverWhitelisted = AllowedGuilds.Contains(guild.Id);
                
                if (!serverWhitelisted)
                {
                    Console.WriteLine($"🚫 Leaving unauthorized server: {guild.Name} (ID: {guild.Id})");
                    
                    try
                    {
                        var systemChannel = guild.SystemChannel ?? guild.DefaultChannel ?? guild.TextChannels.FirstOrDefault();
                        if (systemChannel != null)
                        {
                            var embed = new EmbedBuilder()
                                .WithTitle("🚫 Private Bot - Leaving Server")
                                .WithDescription("Dieser Server gehört deiner damaligen Freundin du Hund. Kickst du sie, kickst du mich auch.")
                                .WithColor(Color.Red)
                                .WithCurrentTimestamp()
                                .Build();
                            
                            await systemChannel.SendMessageAsync(embed: embed);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not send leave message to {guild.Name}: {ex.Message}");
                    }
                    
                    await Task.Delay(3000);
                    await guild.LeaveAsync();
                    await Task.Delay(1000);
                }
                else
                {
                    Console.WriteLine($"✅ Staying in authorized server: {guild.Name} (ID: {guild.Id})");
                }
            }
            
            Console.WriteLine("✅ Server authorization check completed.");
        }
    }
}
