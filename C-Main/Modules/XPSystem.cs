using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using MainbotCSharp.Services;

namespace MainbotCSharp.Modules
{
    public class XPGuildConfig
    {
        public ulong RankChannelId { get; set; } = 0;
        public bool SendLevelUpMessages { get; set; } = true;
        public bool SendRoleMessages { get; set; } = true;
    }

    public class XPSystemCommands : ModuleBase<SocketCommandContext>
    {
        private readonly XPService _xpService;
        
        public XPSystemCommands(XPService xpService)
        {
            _xpService = xpService;
        }

        [Command("run-xp-setup")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task RunXPSetup()
        {
            await ReplyAsync("🚀 **XP-System wird eingerichtet...**\nErstelle alle Rollen im Server und konfiguriere das System...");
            
            var success = await _xpService.SetupXPSystem(Context.Guild);
            
            if (success)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("✅ XP-System erfolgreich eingerichtet!")
                    .WithDescription("**Das XP-System ist jetzt aktiv!**\n\n" +
                        "🎯 **Alle Rollen wurden im Server erstellt:**\n" +
                        "• 202 Military Level-Rollen (Recruit bis Final Authority)\n" +
                        "• 5 Chat-Aktivitäts-Rollen\n" +
                        "• 5 Voice-Aktivitäts-Rollen\n\n" +
                        "⚙️ **Berechtigungen:**\n" +
                        "• Alle Rollen haben Standard-Berechtigungen\n" +
                        "• Admins können Berechtigungen selbst anpassen\n\n" +
                        "📊 **System Features:**\n" +
                        "• Automatisches XP sammeln durch Chat & Voice\n" +
                        "• Military Ranking System (0-201 Level)\n" +
                        "• `!xp` Command zum Status checken\n\n" +
                        "🗑️ **System deaktivieren:** `!remove-xp-setup`\n\n" +
                        "📋 **Möchten Sie ein Rolemanagement-Embed erstellen?**\n" +
                        "Verwenden Sie: `!create-rolemanagement-embed <#channel>`")
                    .WithColor(Color.Green)
                    .WithCurrentTimestamp();
                    
                await ReplyAsync(embed: embed.Build());
            }
            else
            {
                await ReplyAsync("❌ **Fehler beim Einrichten des XP-Systems!**\nBitte prüfen Sie die Bot-Berechtigungen (Rollen verwalten erforderlich).");
            }
        }

        [Command("create-rolemanagement-embed")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task CreateRolemanagementEmbed(ITextChannel channel)
        {
            await ReplyAsync("📋 **Erstelle Rolemanagement-Embed...**");
            
            var success = await _xpService.SendRolemanagementEmbed(Context.Guild, channel);
            
            if (success)
            {
                var confirmEmbed = new EmbedBuilder()
                    .WithTitle("✅ Rolemanagement-Embed gesendet!")
                    .WithDescription($"**Das Rolemanagement-Embed wurde erfolgreich in {channel.Mention} gesendet!**\n\n" +
                        "📋 **Das Embed enthält:**\n" +
                        "• Alle Military Level-Rollen (Recruit bis Final Authority)\n" +
                        "• Alle Nachrichten Rollen (Chat-Aktivität)\n" +
                        "• Alle Sprach Rollen (Voicecalls)\n" +
                        "• Informationen über XP-Sammlung\n\n" +
                        "🎯 **Die Rollen sind direkt verlinkt und klickbar!**")
                    .WithColor(Color.Green)
                    .WithCurrentTimestamp();
                    
                await ReplyAsync(embed: confirmEmbed.Build());
            }
            else
            {
                await ReplyAsync("❌ **Fehler beim Senden des Rolemanagement-Embeds!**\nPrüfen Sie die Bot-Berechtigungen für den Ziel-Channel.");
            }
        }

        [Command("remove-xp-setup")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task RemoveXPSetup()
        {
            await ReplyAsync("🗑️ **XP-System wird deaktiviert...**\nLösche alle XP-Rollen aus dem Server...");
            
            var success = await _xpService.RemoveXPSystem(Context.Guild);
            
            if (success)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("✅ XP-System erfolgreich deaktiviert!")
                    .WithDescription("**Das XP-System wurde komplett entfernt:**\n\n" +
                        "🗑️ **Gelöschte Rollen:**\n" +
                        "• Alle Military Level-Rollen entfernt\n" +
                        "• Alle Chat-Aktivitäts-Rollen entfernt\n" +
                        "• Alle Voice-Aktivitäts-Rollen entfernt\n\n" +
                        "💾 **XP-Daten:**\n" +
                        "• Alle gespeicherten XP-Daten bleiben erhalten\n" +
                        "• Bei erneutem `!run-xp-setup` werden Daten wiederhergestellt\n\n" +
                        "🔄 **System reaktivieren:** `!run-xp-setup`")
                    .WithColor(Color.Orange)
                    .WithCurrentTimestamp();
                    
                await ReplyAsync(embed: embed.Build());
            }
            else
            {
                await ReplyAsync("❌ **Fehler beim Entfernen des XP-Systems!**\nEinige Rollen konnten nicht gelöscht werden. Prüfen Sie die Bot-Berechtigungen.");
            }
        }

        [Command("xp")]
        public async Task CheckXP(SocketUser? user = null)
        {
            user = user ?? Context.User;
            var userData = await _xpService.GetUserData(Context.Guild.Id, user.Id);
            
            var guildUser = user as SocketGuildUser ?? Context.Guild.GetUser(user.Id);
            var displayName = guildUser?.DisplayName ?? user.Username;
            
            var embed = new EmbedBuilder()
                .WithTitle("📊 Your XP-Stats")
                .AddField("XP/ CHAT", userData.MessageXP, true)
                .AddField("XP/ VOICE", userData.VoiceXP, true)
                .AddField("YOUR XP", userData.TotalXP, true)
                .AddField("SENDET", userData.MessageCount, true)
                .AddField("VOICE CALL H", $"{userData.VoiceMinutes / 60:F1}h", true)
                .AddField("YOUR LEVEL", userData.Level, true)
                .WithDescription("Chat and come in voice calls to get more xp's to become a Mgb Legend!\nGo to the corrent rolemanagement channel for more information.")
                .WithColor(0x228B22)
                .WithThumbnailUrl(user.GetAvatarUrl())
                .WithCurrentTimestamp();
                
            await ReplyAsync(embed: embed.Build());
        }

        [Command("xp-give")]
        [Alias("givexp", "addxp")]
        [Summary("Give XP to a user (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task GiveXPAsync(SocketUser user, int amount)
        {
            if (amount <= 0)
            {
                await ReplyAsync("❌ Amount must be greater than 0!");
                return;
            }

            if (amount > 1000000)
            {
                await ReplyAsync("❌ Maximum amount is 1,000,000 XP!");
                return;
            }

            var userData = await _xpService.GetUserData(Context.Guild.Id, user.Id);
            var oldXP = userData.TotalXP;
            var oldLevel = userData.Level;

            // Add XP directly to TotalXP
            userData.TotalXP += amount;
            userData.Level = _xpService.GetLevelFromXP(userData.TotalXP);
            
            _xpService.SaveUserData(Context.Guild.Id, user.Id, userData);

            // Update roles if level changed
            if (userData.Level != oldLevel && user is SocketGuildUser guildUser)
            {
                await _xpService.UpdateUserRoles(guildUser.Guild, guildUser);
            }

            var embed = new EmbedBuilder()
                .WithTitle("✅ XP Given!")
                .WithDescription($"Successfully gave **{amount:N0} XP** to {user.Mention}")
                .AddField("Previous XP", $"{oldXP:N0}", true)
                .AddField("New XP", $"{userData.TotalXP:N0}", true)
                .AddField("Level", $"{oldLevel} → {userData.Level}", true)
                .WithColor(0x00FF00)
                .WithCurrentTimestamp();

            await ReplyAsync(embed: embed.Build());
        }

        [Command("messages-give")]
        [Alias("give-messages", "set-messages")]
        [Summary("Give message count to a user (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task GiveMessagesAsync(SocketUser user, int amount)
        {
            if (amount <= 0)
            {
                await ReplyAsync("❌ Amount must be greater than 0!");
                return;
            }

            if (amount > 100000)
            {
                await ReplyAsync("❌ Maximum amount is 100,000 messages!");
                return;
            }

            var userData = await _xpService.GetUserData(Context.Guild.Id, user.Id);
            var oldMessages = userData.MessageCount;
            var oldXP = userData.MessageXP;

            // Set message count directly
            userData.MessageCount = amount;
            userData.MessageXP = _xpService.CalculateMessageXP(userData.MessageCount);
            userData.UpdateTotalXP();
            userData.Level = _xpService.GetLevelFromXP(userData.TotalXP);
            
            _xpService.SaveUserData(Context.Guild.Id, user.Id, userData);

            // Update roles
            if (user is SocketGuildUser guildUser)
            {
                await _xpService.UpdateUserRoles(guildUser.Guild, guildUser);
            }

            var embed = new EmbedBuilder()
                .WithTitle("✅ Messages Set!")
                .WithDescription($"Successfully set **{amount:N0} messages** for {user.Mention}")
                .AddField("Previous Messages", $"{oldMessages:N0}", true)
                .AddField("New Messages", $"{userData.MessageCount:N0}", true)
                .AddField("Message XP", $"{oldXP:N0} → {userData.MessageXP:N0}", true)
                .AddField("Total XP", $"{userData.TotalXP:N0}", true)
                .AddField("Level", $"{userData.Level}", true)
                .WithColor(0x00FF00)
                .WithCurrentTimestamp();

            await ReplyAsync(embed: embed.Build());
        }

        [Command("voice-give")]
        [Alias("give-voice", "set-voice")]
        [Summary("Give voice hours to a user (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task GiveVoiceAsync(SocketUser user, int hours)
        {
            if (hours <= 0)
            {
                await ReplyAsync("❌ Hours must be greater than 0!");
                return;
            }

            if (hours > 10000)
            {
                await ReplyAsync("❌ Maximum amount is 10,000 hours!");
                return;
            }

            var userData = await _xpService.GetUserData(Context.Guild.Id, user.Id);
            var oldHours = userData.VoiceMinutes / 60;
            var oldXP = userData.VoiceXP;

            // Set voice minutes directly (convert hours to minutes)
            userData.VoiceMinutes = hours * 60;
            userData.VoiceXP = _xpService.CalculateVoiceXP(userData.VoiceMinutes);
            userData.UpdateTotalXP();
            userData.Level = _xpService.GetLevelFromXP(userData.TotalXP);
            
            _xpService.SaveUserData(Context.Guild.Id, user.Id, userData);

            // Update roles
            if (user is SocketGuildUser guildUser)
            {
                await _xpService.UpdateUserRoles(guildUser.Guild, guildUser);
            }

            var embed = new EmbedBuilder()
                .WithTitle("✅ Voice Hours Set!")
                .WithDescription($"Successfully set **{hours:N0} hours** for {user.Mention}")
                .AddField("Previous Hours", $"{oldHours:F1}h", true)
                .AddField("New Hours", $"{hours:N0}h", true)
                .AddField("Voice XP", $"{oldXP:N0} → {userData.VoiceXP:N0}", true)
                .AddField("Total XP", $"{userData.TotalXP:N0}", true)
                .AddField("Level", $"{userData.Level}", true)
                .WithColor(0x00FF00)
                .WithCurrentTimestamp();

            await ReplyAsync(embed: embed.Build());
        }

        [Command("set-rank-channel")]
        [Alias("set-level-channel", "rank-channel")]
        [Summary("Setze den Channel für Level-Up Nachrichten")]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        public async Task SetRankChannelAsync(ITextChannel? channel = null)
        {
            if (channel == null)
            {
                var config = _xpService.GetGuildConfig(Context.Guild.Id);
                if (config.RankChannelId == 0)
                {
                    var helpEmbed = new EmbedBuilder()
                        .WithTitle("📈 Rank-Channel Setup")
                        .WithDescription("**Setze einen Channel für Level-Up Nachrichten:**\n\n" +
                                       "**Verwendung:**\n" +
                                       "`!set-rank-channel #channel` - Setze Channel für Level-Up Nachrichten\n" +
                                       "`!rank-channel-off` - Level-Up Nachrichten deaktivieren\n" +
                                       "`!rank-channel-status` - Aktuellen Status anzeigen\n\n" +
                                       "**Features:**\n" +
                                       "• 🎉 Level-Up Nachrichten\n" +
                                       "• 💬 Chat-Rollen Nachrichten\n" +
                                       "• 🎤 Voice-Rollen Nachrichten\n" +
                                       "• 🎨 Schöne Embeds mit Fortschritt")
                        .WithColor(0x40E0D0)
                        .Build();
                    
                    await ReplyAsync(embed: helpEmbed);
                    return;
                }
                else
                {
                    var currentChannel = Context.Guild.GetTextChannel(config.RankChannelId);
                    await ReplyAsync($"✅ Aktueller Rank-Channel: {currentChannel?.Mention ?? "Unbekannt"}");
                    return;
                }
            }

            _xpService.SetRankChannel(Context.Guild.Id, channel.Id);
            
            var embed = new EmbedBuilder()
                .WithTitle("📈 Rank-Channel gesetzt!")
                .WithDescription($"**Level-Up Nachrichten werden jetzt in {channel.Mention} gesendet!**\n\n" +
                               "🎉 **Was wird gesendet:**\n" +
                               "• Level-Up Benachrichtigungen\n" +
                               "• Chat-Aktivitäts-Rollen\n" +
                               "• Voice-Aktivitäts-Rollen\n\n" +
                               "⚙️ **Einstellungen:**\n" +
                               "`!rank-channel-off` - Nachrichten deaktivieren\n" +
                               "`!rank-channel-status` - Status anzeigen")
                .WithColor(Color.Green)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
            
            await ReplyAsync(embed: embed);
            
            // Test-Nachricht senden
            var testEmbed = new EmbedBuilder()
                .WithTitle("🎉 Rank-System aktiviert!")
                .WithDescription($"Dieser Channel wurde als **Level-Up Channel** eingerichtet!\n\n" +
                               $"Level-Up Nachrichten werden hier angezeigt wenn Mitglieder:\n" +
                               $"• 📈 Ein neues Level erreichen\n" +
                               $"• 💬 Eine neue Chat-Rolle erhalten\n" +
                               $"• 🎤 Eine neue Voice-Rolle erhalten")
                .WithColor(0x40E0D0)
                .WithFooter("Code.Master() XP System")
                .WithCurrentTimestamp()
                .Build();
            
            await channel.SendMessageAsync(embed: testEmbed);
        }

        [Command("rank-channel-off")]
        [Alias("disable-rank-channel")]
        [Summary("Deaktiviere Level-Up Nachrichten")]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        public async Task DisableRankChannelAsync()
        {
            _xpService.SetRankChannel(Context.Guild.Id, 0);
            
            var embed = new EmbedBuilder()
                .WithTitle("📈 Rank-Channel deaktiviert")
                .WithDescription("**Level-Up Nachrichten wurden deaktiviert.**\n\n" +
                               "Das XP-System funktioniert weiterhin, aber es werden keine\n" +
                               "Nachrichten mehr gesendet.\n\n" +
                               "**Reaktivieren:** `!set-rank-channel #channel`")
                .WithColor(Color.Orange)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
            
            await ReplyAsync(embed: embed);
        }

        [Command("rank-channel-status")]
        [Alias("rank-status")]
        [Summary("Zeige Status des Rank-Channels")]
        public async Task RankChannelStatusAsync()
        {
            var config = _xpService.GetGuildConfig(Context.Guild.Id);
            
            var embed = new EmbedBuilder()
                .WithTitle("📈 Rank-Channel Status")
                .WithColor(0x40E0D0)
                .WithTimestamp(DateTimeOffset.UtcNow);
            
            if (config.RankChannelId == 0)
            {
                embed.WithDescription("❌ **Kein Rank-Channel gesetzt**\n\n" +
                                    "Level-Up Nachrichten sind deaktiviert.\n" +
                                    "**Setup:** `!set-rank-channel #channel`");
            }
            else
            {
                var channel = Context.Guild.GetTextChannel(config.RankChannelId);
                if (channel != null)
                {
                    embed.WithDescription($"✅ **Rank-Channel aktiv**\n\n" +
                                        $"**Channel:** {channel.Mention}\n" +
                                        $"**Level-Up Nachrichten:** {(config.SendLevelUpMessages ? "✅ An" : "❌ Aus")}\n" +
                                        $"**Rollen-Nachrichten:** {(config.SendRoleMessages ? "✅ An" : "❌ Aus")}\n\n" +
                                        $"**Deaktivieren:** `!rank-channel-off`");
                }
                else
                {
                    embed.WithDescription("⚠️ **Channel nicht gefunden**\n\n" +
                                        $"Der eingestellte Channel (ID: {config.RankChannelId}) existiert nicht mehr.\n" +
                                        $"**Neu setzen:** `!set-rank-channel #channel`");
                }
            }
            
            await ReplyAsync(embed: embed.Build());
        }

        [Command("xp-debug")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task DebugXPRoles()
        {
            var existingRoles = Context.Guild.Roles.Where(r => 
                _xpService.IsXPRole(r.Name)).ToList();
                
            var embed = new EmbedBuilder()
                .WithTitle("🔍 XP-Rollen Debug")
                .WithDescription($"**Gefundene XP-Rollen im Server:** {existingRoles.Count}\n\n" +
                    string.Join("\n", existingRoles.Select(r => $"• {r.Name} (ID: {r.Id})")))
                .WithColor(Color.Orange);
                
            await ReplyAsync(embed: embed.Build());
        }
    }

    public class XPService
    {
        private const string XP_DATA_FILE = "Modules/xp_data.json";
        private const string GUILD_CONFIG_FILE = "Modules/guild_xp_configs.json";
        private readonly DiscordSocketClient _client;
        private Dictionary<string, UserXPData> _userData = new();
        private Dictionary<ulong, XPGuildConfig> _guildConfigs = new();
        
        // XP-System Konfiguration - MEMORY OPTIMIZED Military Ranking System 
        // Reduced from 10,000 levels to 100 levels (Max: 10,000 XP instead of 1,000,000)
        // Each level = 100 XP, Much more reasonable memory usage!
        private readonly Dictionary<string, (int minLevel, int maxLevel, int minXP, int maxXP)> _levelRoles = new()
        {
            { "Lil' Soldat 🧸", (1, 10, 100, 1000) },          // Levels 1-10
            { "Frontkämpfi 🐣", (11, 20, 1100, 2000) },        // Levels 11-20  
            { "Pew-Pew Schütze ✨", (21, 35, 2100, 3500) },    // Levels 21-35
            { "Stoßtruppi 💥", (36, 50, 3600, 5000) },         // Levels 36-50
            { "Truppführerchen 🐻", (51, 65, 5100, 6500) },    // Levels 51-65
            { "Einsatz-Offi 🧠💕", (66, 80, 6600, 8000) },     // Levels 66-80
            { "Oberkommando-Knuffi 👑💕", (81, 95, 8100, 9500) }, // Levels 81-95
            { "Baby Brigadeführer 🍼👼", (96, 100, 9600, 10000) } // Levels 96-100 (MAX)
        };
        
        private readonly Dictionary<string, int> _messageRoles = new()
        {
            { "Cadet de Plume", 300 },
            { "Soldat de Correspondance", 1000 },
            { "Caporal des Mots", 2000 },
            { "Sergent de Communication", 3000 },
            { "Commandant du Dialogue", 5000 }
        };
        
        private readonly Dictionary<string, int> _voiceRoles = new()
        {
            { "Soldato", 50 * 60 },      // 50 Stunden in Minuten
            { "Caporale", 150 * 60 },    // 150 Stunden
            { "Sergente", 300 * 60 },    // 300 Stunden
            { "Tenente", 600 * 60 },     // 600 Stunden
            { "Capitano", 1000 * 60 }    // 1000 Stunden
        };

        public XPService(DiscordSocketClient client)
        {
            _client = client;
            LoadXPData();
            LoadGuildConfigs();
        }

        public async Task<bool> SetupXPSystem(SocketGuild guild)
        {
            try
            {
                Console.WriteLine("🚀 Setting up XP System...");
                
                // Erstelle alle 12 Military Level-Rollen im Discord-Server
                Console.WriteLine($"🎖️ Creating {_levelRoles.Count} military roles...");
                foreach (var role in _levelRoles)
                {
                    var roleName = role.Key; // Der Rollen-Name ist jetzt der Key
                    if (guild.Roles.Any(r => r.Name == roleName)) continue;
                    
                    await guild.CreateRoleAsync(roleName, permissions: GuildPermissions.None, 
                        color: GetRoleColor(role.Value.maxXP), isMentionable: false);
                    Console.WriteLine($"✅ Created military role: {roleName}");
                    await Task.Delay(500); // Rate limit protection
                }
                
                // Erstelle Chat-Rollen im Discord-Server
                Console.WriteLine($"📝 Creating {_messageRoles.Count} message roles...");
                foreach (var roleData in _messageRoles)
                {
                    var roleName = roleData.Key;
                    Console.WriteLine($"🔍 Checking message role: {roleName}");
                    
                    if (guild.Roles.Any(r => r.Name == roleName)) 
                    {
                        Console.WriteLine($"⏭️ Message role already exists: {roleName}");
                        continue;
                    }
                    
                    var createdRole = await guild.CreateRoleAsync(roleName, permissions: GuildPermissions.None, 
                        color: Color.Blue, isMentionable: false);
                    Console.WriteLine($"✅ Created message role: {roleName} (ID: {createdRole.Id})");
                    await Task.Delay(500);
                }
                
                // Erstelle Voice-Rollen im Discord-Server
                Console.WriteLine($"🎤 Creating {_voiceRoles.Count} voice roles...");
                foreach (var roleData in _voiceRoles)
                {
                    var roleName = roleData.Key;
                    Console.WriteLine($"🔍 Checking voice role: {roleName}");
                    
                    if (guild.Roles.Any(r => r.Name == roleName)) 
                    {
                        Console.WriteLine($"⏭️ Voice role already exists: {roleName}");
                        continue;
                    }
                    
                    var createdRole = await guild.CreateRoleAsync(roleName, permissions: GuildPermissions.None, 
                        color: Color.Purple, isMentionable: false);
                    Console.WriteLine($"✅ Created voice role: {roleName} (ID: {createdRole.Id})");
                    await Task.Delay(500);
                }
                
                Console.WriteLine($"🎉 XP-System setup complete! Created {_levelRoles.Count + _messageRoles.Count + _voiceRoles.Count} roles in server.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ XP Setup error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveXPSystem(SocketGuild guild)
        {
            try
            {
                Console.WriteLine("🗑️ Removing XP System...");
                int deletedCount = 0;
                
                // Lösche alle Military Level-Rollen aus dem Discord-Server
                foreach (var roleName in _levelRoles.Keys)
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == roleName);
                    if (role != null)
                    {
                        await role.DeleteAsync();
                        Console.WriteLine($"🗑️ Deleted level role: {roleName}");
                        deletedCount++;
                        await Task.Delay(500); // Rate limit protection
                    }
                }
                
                // Lösche alle Chat-Rollen aus dem Discord-Server
                foreach (var roleName in _messageRoles.Keys)
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == roleName);
                    if (role != null)
                    {
                        await role.DeleteAsync();
                        Console.WriteLine($"🗑️ Deleted message role: {roleName}");
                        deletedCount++;
                        await Task.Delay(500);
                    }
                }
                
                // Lösche alle Voice-Rollen aus dem Discord-Server
                foreach (var roleName in _voiceRoles.Keys)
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == roleName);
                    if (role != null)
                    {
                        await role.DeleteAsync();
                        Console.WriteLine($"🗑️ Deleted voice role: {roleName}");
                        deletedCount++;
                        await Task.Delay(500);
                    }
                }
                
                Console.WriteLine($"🎉 XP-System removal complete! Deleted {deletedCount} roles from server.");
                // Note: XP-Daten bleiben gespeichert für eventuelles Wiederherstellen
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ XP Removal error: {ex.Message}");
                return false;
            }
        }

        public async Task HandleMessage(SocketUserMessage message)
        {
            if (message.Author.IsBot) return;
            
            var userId = message.Author.Id;
            var guildId = ((SocketGuildChannel)message.Channel).Guild.Id;
            
            var userData = await GetUserData(guildId, userId);
            userData.MessageCount++;
            userData.MessageXP = CalculateMessageXP(userData.MessageCount);
            userData.UpdateTotalXP();
            
            SaveUserData(guildId, userId, userData);
            if (message.Author is SocketGuildUser guildUser)
            {
                await UpdateUserRoles(((SocketGuildChannel)message.Channel).Guild, guildUser);
            }
        }

        public async Task HandleVoiceUpdate(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            if (user.IsBot) return;
            
            var guildId = before.VoiceChannel?.Guild?.Id ?? after.VoiceChannel?.Guild?.Id;
            if (guildId == null) return;
            
            var userData = await GetUserData(guildId.Value, user.Id);
            
            // Voice tracking logic would go here
            // For now, we'll add a small amount of voice time when they join/leave
            if (after.VoiceChannel != null && before.VoiceChannel == null)
            {
                // User joined voice - start tracking
                userData.LastVoiceJoin = DateTimeOffset.UtcNow;
            }
            else if (before.VoiceChannel != null && after.VoiceChannel == null)
            {
                // User left voice - calculate time
                if (userData.LastVoiceJoin.HasValue)
                {
                    var duration = DateTimeOffset.UtcNow - userData.LastVoiceJoin.Value;
                    userData.VoiceMinutes += (int)duration.TotalMinutes;
                    userData.VoiceXP = CalculateVoiceXP(userData.VoiceMinutes);
                    userData.UpdateTotalXP();
                    userData.LastVoiceJoin = null;
                    
                    SaveUserData(guildId.Value, user.Id, userData);
                    
                    if (user is SocketGuildUser guildUser)
                    {
                        await UpdateUserRoles(guildUser.Guild, guildUser);
                    }
                }
            }
        }

        public async Task RestoreVoiceSessions(IReadOnlyCollection<SocketGuild> guilds)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var guild in guilds)
            {
                foreach (var channel in guild.VoiceChannels)
                {
                    foreach (var member in channel.Users)
                    {
                        if (member.IsBot) continue;
                        var userData = await GetUserData(guild.Id, member.Id);
                        if (!userData.LastVoiceJoin.HasValue)
                        {
                            userData.LastVoiceJoin = now;
                            SaveUserData(guild.Id, member.Id, userData);
                            Console.WriteLine($"Restored voice session for {member.Username} in {channel.Name}");
                        }
                    }
                }
            }
        }

        public async Task UpdateUserRoles(SocketGuild guild, SocketGuildUser user)
        {
            var userData = await GetUserData(guild.Id, user.Id);
            
            try
            {
                // Update military level roles (neues System)
                var currentLevel = GetLevelFromXP(userData.TotalXP);
                var targetRoleName = GetCurrentMilitaryRole(currentLevel);
                
                var targetRole = guild.Roles.FirstOrDefault(r => r.Name == targetRoleName);
                
                if (targetRole != null && !user.Roles.Contains(targetRole))
                {
                    // Remove old military roles
                    var oldMilitaryRoles = user.Roles.Where(r => _levelRoles.ContainsKey(r.Name));
                    foreach (var oldRole in oldMilitaryRoles)
                    {
                        await user.RemoveRoleAsync(oldRole);
                    }
                    
                    await user.AddRoleAsync(targetRole);
                    Console.WriteLine($"🎉 {user.DisplayName} erreichte Level {currentLevel} ({targetRoleName})!");
                    
                    // Send level up message to rank channel
                    await SendLevelUpMessage(guild, user, currentLevel, targetRoleName);
                }
                
                // Update message roles
                var targetMessageRole = _messageRoles.Where(kvp => kvp.Value <= userData.MessageCount)
                                                    .OrderByDescending(kvp => kvp.Value)
                                                    .FirstOrDefault();
                
                if (!string.IsNullOrEmpty(targetMessageRole.Key))
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == targetMessageRole.Key);
                    if (role != null && !user.Roles.Contains(role))
                    {
                        var oldMessageRoles = user.Roles.Where(r => _messageRoles.ContainsKey(r.Name));
                        foreach (var oldRole in oldMessageRoles)
                        {
                            await user.RemoveRoleAsync(oldRole);
                        }
                        
                        await user.AddRoleAsync(role);
                        Console.WriteLine($"💬 {user.DisplayName} erreichte Message-Rolle: {targetMessageRole.Key}!");
                        
                        // Send role message to rank channel
                        await SendRoleMessage(guild, user, "Message", targetMessageRole.Key);
                    }
                }
                
                // Update voice roles
                var voiceHours = userData.VoiceMinutes / 60;
                var targetVoiceRole = _voiceRoles.Where(kvp => kvp.Value <= userData.VoiceMinutes)
                                                .OrderByDescending(kvp => kvp.Value)
                                                .FirstOrDefault();
                
                if (!string.IsNullOrEmpty(targetVoiceRole.Key))
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == targetVoiceRole.Key);
                    if (role != null && !user.Roles.Contains(role))
                    {
                        var oldVoiceRoles = user.Roles.Where(r => _voiceRoles.ContainsKey(r.Name));
                        foreach (var oldRole in oldVoiceRoles)
                        {
                            await user.RemoveRoleAsync(oldRole);
                        }
                        
                        await user.AddRoleAsync(role);
                        Console.WriteLine($"🎤 {user.DisplayName} erreichte Voice-Rolle: {targetVoiceRole.Key}!");
                        
                        // Send role message to rank channel
                        await SendRoleMessage(guild, user, "Voice", targetVoiceRole.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error updating roles for {user.DisplayName}: {ex.Message}");
            }
        }

        private async Task SendLevelUpMessage(SocketGuild guild, SocketGuildUser user, int newLevel, string roleName)
        {
            try
            {
                var config = GetGuildConfig(guild.Id);
                if (config.RankChannelId == 0 || !config.SendLevelUpMessages) return;
                
                var channel = guild.GetTextChannel(config.RankChannelId);
                if (channel == null) return;

                var embed = new EmbedBuilder()
                    .WithTitle("🎆 Level Up!")
                    .WithDescription($"**{user.DisplayName} hat Level {newLevel} erreicht!**\n\n" +
                                   $"🏆 **Neue Rolle:** {roleName}\n" +
                                   $"📈 **Level:** {newLevel}\n" +
                                   $"🎉 Herzlichen Glückwunsch!")
                    .WithColor(Color.Gold)
                    .WithThumbnailUrl(user.GetAvatarUrl())
                    .WithFooter($"Level {newLevel}")
                    .WithCurrentTimestamp()
                    .Build();

                await channel.SendMessageAsync(user.Mention, embed: embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending level up message: {ex.Message}");
            }
        }

        private async Task SendRoleMessage(SocketGuild guild, SocketGuildUser user, string roleType, string roleName)
        {
            try
            {
                var config = GetGuildConfig(guild.Id);
                if (config.RankChannelId == 0 || !config.SendRoleMessages) return;
                
                var channel = guild.GetTextChannel(config.RankChannelId);
                if (channel == null) return;

                var emoji = roleType == "Message" ? "💬" : "🎤";
                var activityType = roleType == "Message" ? "Chat-Aktivität" : "Voice-Aktivität";
                
                var embed = new EmbedBuilder()
                    .WithTitle($"{emoji} Neue Rolle erhalten!")
                    .WithDescription($"**{user.Mention} hat eine neue {activityType}s-Rolle erhalten!**\n\n" +
                                   $"🎯 **Rolle:** {roleName}\n" +
                                   $"{emoji} **Kategorie:** {activityType}\n" +
                                   $"🎉 Weiter so!")
                    .WithColor(roleType == "Message" ? Color.Blue : Color.Purple)
                    .WithThumbnailUrl(user.GetAvatarUrl())
                    .WithFooter($"{activityType}s-Rolle")
                    .WithCurrentTimestamp()
                    .Build();

                await channel.SendMessageAsync(embed: embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending role message: {ex.Message}");
            }
        }

        public XPGuildConfig GetGuildConfig(ulong guildId)
        {
            if (!_guildConfigs.ContainsKey(guildId))
            {
                _guildConfigs[guildId] = new XPGuildConfig();
                SaveGuildConfigs();
            }
            return _guildConfigs[guildId];
        }

        public void SetRankChannel(ulong guildId, ulong channelId)
        {
            var config = GetGuildConfig(guildId);
            config.RankChannelId = channelId;
            SaveGuildConfigs();
            Console.WriteLine($"Rank channel set for guild {guildId}: {channelId}");
        }

        private void LoadGuildConfigs()
        {
            try
            {
                if (File.Exists(GUILD_CONFIG_FILE))
                {
                    var json = File.ReadAllText(GUILD_CONFIG_FILE);
                    var configs = JsonSerializer.Deserialize<Dictionary<ulong, XPGuildConfig>>(json);
                    if (configs != null)
                    {
                        foreach (var kvp in configs)
                        {
                            _guildConfigs[kvp.Key] = kvp.Value;
                        }
                    }
                }
                Console.WriteLine($"Loaded {_guildConfigs.Count} guild XP configurations");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading guild configs: {ex.Message}");
            }
        }

        private void SaveGuildConfigs()
        {
            try
            {
                var json = JsonSerializer.Serialize(_guildConfigs, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(GUILD_CONFIG_FILE, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving guild configs: {ex.Message}");
            }
        }

        public async Task<UserXPData> GetUserData(ulong guildId, ulong userId)
        {
            var key = $"{guildId}_{userId}";
            if (!_userData.ContainsKey(key))
            {
                _userData[key] = new UserXPData();
            }
            return _userData[key];
        }
        
        public bool IsXPRole(string roleName)
        {
            return _levelRoles.ContainsKey(roleName) ||
                   _messageRoles.ContainsKey(roleName) ||
                   _voiceRoles.ContainsKey(roleName);
        }

        public async Task<bool> SendRolemanagementEmbed(SocketGuild guild, ITextChannel channel)
        {
            try
            {
                Console.WriteLine("📋 Creating Rolemanagement Embed...");
                
                // Baue alle 12 Military Rollen String
                var militaryRolesText = "";
                foreach (var role in _levelRoles.OrderBy(lr => lr.Value.minLevel))
                {
                    var roleName = role.Key;
                    var roleData = role.Value;
                    
                    var discordRole = guild.Roles.FirstOrDefault(r => r.Name == roleName);
                    var roleMention = discordRole != null ? discordRole.Mention : roleName;
                    
                    militaryRolesText += $"• **Level {roleData.minLevel}-{roleData.maxLevel}** ({roleData.minXP:N0}-{roleData.maxXP:N0} XP) → {roleMention}\n";
                }
                
                // Baue Nachrichten Rollen String  
                var messageRolesText = "";
                foreach (var msgRole in _messageRoles.OrderBy(mr => mr.Value))
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == msgRole.Key);
                    var roleMention = role != null ? role.Mention : msgRole.Key;
                    messageRolesText += $"• {msgRole.Value:N0} Nachrichten → {roleMention}\n";
                }
                
                // Baue Voice Rollen String
                var voiceRolesText = "";
                foreach (var voiceRole in _voiceRoles.OrderBy(vr => vr.Value))
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == voiceRole.Key);
                    var roleMention = role != null ? role.Mention : voiceRole.Key;
                    var hours = voiceRole.Value / 60;
                    voiceRolesText += $"• {hours} Stunden → {roleMention}\n";
                }
                
                // Erstes und einziges Embed - Alle Rollen zusammen
                var embed = new EmbedBuilder()
                    .WithTitle("📋 MGB ROLEMANAGEMENT")
                    .WithDescription("Climb the ranks through combat and dedication!\n" +
                        "───────────────────────╭────╮───────────────────────\n\n" +
                        "**💬 How to earn XP:**\n" +
                        "• Send messages in chat channels (5 XP per message + bonuses)\n" +
                        "• Join voice channels and talk (5 XP per hour + bonuses)\n" +
                        "• Be active and participate in the community!\n\n" +
                        "**📊 Check your progress:** Use `!xp` to see your stats\n" +
                        "**🎯 System:** Every level = 100 XP | Level 1-10.000 (Max: 1.000.000 XP)")
                    .AddField("⚔️ Military Ranks", 
                        "These ranks are earned through **total XP** (Chat + Voice combined):\n\n" + militaryRolesText, false)
                    .AddField("💬 Chat Activity Ranks (Französisch)", 
                        "These roles are earned through written messages:\n\n" + messageRolesText, true)
                    .AddField("🎙️ Voice Activity Ranks (Italienisch)", 
                        "These roles are earned through voice chat hours:\n\n" + voiceRolesText, true)
                    .AddField("🎮 Endless Mode", 
                        "**After Level 10.000:**\n" +
                        "• Keep the final military role forever\n" +
                        "• Continue gaining XP and levels\n" +
                        "• Ultimate bragging rights!", false)
                    .WithColor(0x8A2BE2) // Purple
                    .WithFooter("Complete Role Overview • Automatic Updates • Endless Progression")
                    .WithCurrentTimestamp();
                
                // Sende das eine Embed
                await channel.SendMessageAsync(embed: embed.Build());
                
                Console.WriteLine($"✅ Complete rolemanagement embed sent to #{channel.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error sending rolemanagement embed: {ex.Message}");
                return false;
            }
        }
        
        public void SaveUserData(ulong guildId, ulong userId, UserXPData data)
        {
            var key = $"{guildId}_{userId}";
            _userData[key] = data;
            SaveXPData();
        }

        public int CalculateMessageXP(int messageCount)
        {
            // 5 XP pro Nachricht, plus 20 XP Bonus alle 100 Nachrichten
            return (messageCount * 5) + (messageCount / 100) * 20;
        }

        public int CalculateVoiceXP(int voiceMinutes)
        {
            // 5 XP pro Stunde (60 Minuten), plus 20 XP Bonus alle 10 Stunden
            int hours = voiceMinutes / 60;
            return (hours * 5) + (hours / 10) * 20;
        }

        public int GetLevelFromXP(int xp)
        {
            // Neues System: Jedes Level = 100 XP
            // Level 0 = 0 XP, Level 1 = 100 XP, Level 2 = 200 XP, etc.
            // Minimum ist Level 1 (nicht Level 0)
            return Math.Max(1, xp / 100);
        }
        
        // Ermittelt die aktuelle Militär-Rolle basierend auf dem Level
        public string GetCurrentMilitaryRole(int level)
        {
            // Endless Mode: Nach Level 10.000 bleibt "Baby Brigadeführer" für immer
            if (level >= 10000)
            {
                return "Baby Brigadeführer 🍼👼";
            }
            
            // Finde die passende Rolle für das aktuelle Level
            foreach (var role in _levelRoles)
            {
                if (level >= role.Value.minLevel && level <= role.Value.maxLevel)
                {
                    return role.Key;
                }
            }
            
            // Fallback: Wenn Level unter 1, dann erste Rolle
            return "Lil' Soldat 🧸";
        }

        private Color GetRoleColor(int xp)
        {
            // Neue Farben basierend auf XP-Bereichen der 12 Rollen
            return xp switch
            {
                < 83300 => Color.LightGrey,        // Lil' Soldat
                < 166600 => Color.Green,           // Frontkämpfi  
                < 249900 => Color.Blue,            // Pew-Pew Schütze
                < 333200 => Color.Purple,          // Stoßtruppi
                < 416500 => Color.Orange,          // Truppführerchen
                < 499800 => Color.Red,             // Einsatz-Offi
                < 583100 => Color.Magenta,         // Feldkommandi
                < 666400 => Color.Gold,            // Einsatz-Leiti
                < 749700 => Color.DarkBlue,        // High-Offi
                < 833000 => Color.DarkRed,         // Oberkommando-Knuffi
                < 916300 => Color.DarkMagenta,     // Sweet Landa
                _ => Color.Gold                     // Baby Brigadeführer (Endrolle)
            };
        }

        private void LoadXPData()
        {
            try
            {
                if (File.Exists(XP_DATA_FILE))
                {
                    var json = File.ReadAllText(XP_DATA_FILE);
                    _userData = JsonSerializer.Deserialize<Dictionary<string, UserXPData>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading XP data: {ex.Message}");
                _userData = new();
            }
        }

        private void SaveXPData()
        {
            try
            {
                // MEMORY OPTIMIZATION: Cleanup before saving
                CleanupInactiveUsers();
                
                var json = JsonSerializer.Serialize(_userData, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(XP_DATA_FILE, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error saving XP data: {ex.Message}");
            }
        }
        
        private void CleanupInactiveUsers()
        {
            try
            {
                // Remove users with 0 XP and no recent activity (over 30 days old)
                var cutoffDate = DateTimeOffset.UtcNow.AddDays(-30);
                var inactiveUsers = _userData
                    .Where(kvp => kvp.Value.TotalXP == 0 && 
                                 (!kvp.Value.LastVoiceJoin.HasValue || kvp.Value.LastVoiceJoin.Value < cutoffDate))
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var userId in inactiveUsers)
                    _userData.Remove(userId);
                
                // Limit total users to prevent excessive memory usage
                const int MAX_USERS = 1000; // Reduced from 10000 to 1000 for memory optimization
                if (_userData.Count > MAX_USERS)
                {
                    var usersToRemove = _userData
                        .OrderBy(kvp => kvp.Value.TotalXP) // Remove users with lowest XP first
                        .Take(_userData.Count - MAX_USERS)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var userId in usersToRemove)
                        _userData.Remove(userId);
                }
                
                if (inactiveUsers.Count > 0)
                    Console.WriteLine($"XP Cleanup: Removed {inactiveUsers.Count} inactive users. Total users: {_userData.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during XP cleanup: {ex.Message}");
            }
        }
    }

    public class UserXPData
    {
        public int MessageCount { get; set; } = 0;
        public int MessageXP { get; set; } = 0;
        public int VoiceMinutes { get; set; } = 0;
        public int VoiceXP { get; set; } = 0;
        public int TotalXP { get; set; } = 0;
        public int Level { get; set; } = 0;
        public DateTimeOffset? LastVoiceJoin { get; set; }

        public void UpdateTotalXP()
        {
            TotalXP = MessageXP + VoiceXP;
        }
    }
}