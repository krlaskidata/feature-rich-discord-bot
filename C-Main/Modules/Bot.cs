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
        private const string BOT_INVITE_LINK = "https://discord.com/oauth2/authorize?client_id=1435244593301159978&permissions=8&integration_type=0&scope=bot";
        
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
                Console.WriteLine("ERROR: Please set environment variable MAINBOT_TOKEN with your bot token.");
                return;
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

            // PingService is now handled by separate C-Ping bot
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
            Console.WriteLine($"[DEBUG] Message received: Author={rawMessage.Author.Username} ({rawMessage.Author.Id}), Channel={rawMessage.Channel.Id}, Content='{rawMessage.Content}', IsBot={rawMessage.Author.IsBot}");
            
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
            catch { }

            if (!(rawMessage is SocketUserMessage message)) return;
            if (message.Author.IsBot) return;

            try
            {
                var xpService = _services.GetService<XPService>();
                await xpService.HandleMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("XPService HandleMessage error: " + ex);
            }

            int argPos = 0;
            if (!(message.HasCharPrefix('!', ref argPos) || message.HasMentionPrefix(_client.CurrentUser, ref argPos)))
            {
                Console.WriteLine($"[DEBUG] Message '{message.Content}' doesn't have command prefix, but MessageReceived event was fired");
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
                "gm", "gn", "hi"
            };
            
            if (!AuthorizedUsers.Contains(message.Author.Id) && !allowedForEveryone.Contains(commandName))
            {
                Console.WriteLine($"[AUTH] Unauthorized user {message.Author.Username} ({message.Author.Id}) tried to use command: {message.Content}");
                return;
            }

            if (message.Content.ToLower().StartsWith("!invite") && IsBotOwner(message.Author.Id))
            {
                await HandleInviteCommand(message);
                return;
            }

            var context = new SocketCommandContext(_client, message);

            await _commands.ExecuteAsync(context, argPos, _services);
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

        private static readonly ulong[] AuthorizedUsers = {
            REMOVED_USER_IDUL,
        };

        private static readonly ulong[] BotOwners = {
            REMOVED_USER_IDUL,
        };

        public static bool IsBotOwner(ulong userId)
        {
            return BotOwners.Contains(userId);
        }

        private static readonly ulong[] AllowedGuilds = {
            REMOVED_GUILD_IDUL,
            REMOVED_GUILD_IDUL,
            REMOVED_GUILD_IDUL,
            REMOVED_GUILD_IDUL,
            REMOVED_GUILD_IDUL,
            REMOVED_GUILD_IDUL,
            1226105316404498475UL
        };

        private static readonly Dictionary<string, string> MainBotInvites = new Dictionary<string, string>
        {
            { "main1", "REMOVED_INVITE" },
            { "main2", "REMOVED_INVITE" },
            { "testing", "REMOVED_INVITE" },
            { "coding", "REMOVED_INVITE" },
            { "tropia", "REMOVED_INVITE" },
            { "friend", "REMOVED_INVITE" }
        };

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
                            .WithDescription("This bot is private and can only be used in authorized servers.\n\nThe bot will now leave this server.")
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
                                .WithDescription("This bot is now set to private mode and can only be used in authorized servers.\n\nThe bot will leave this server.")
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
