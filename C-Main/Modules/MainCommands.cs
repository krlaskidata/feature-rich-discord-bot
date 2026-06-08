using System.Threading.Tasks;
using Discord.Commands;
using Discord;
using Discord.WebSocket;
using MainbotCSharp.Services;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.IO;

namespace MainbotCSharp.Modules
{
    public class MainCommands : ModuleBase<SocketCommandContext>
    {
        [Command("ping")]
        [Summary("Check bot latency")]
        public async Task PingAsync()
        {
            var latency = Context.Client.Latency;
            await ReplyAsync($"🏓 Pong! Latency: {latency}ms");
        }

        [Command("help")]
        [Summary("Shows all available commands")]
        public async Task HelpAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("**Code.Master()**")
                .WithColor(0x40E0D0)
                .WithDescription("*This bot is a community bot and can only be used upon admin request. *More features, better performance, superior design!*")
                .AddField("★ **Security Commands**",
                    "**Setup / Status**\n" +
                    "`!setsecuritymod` - Security einrichten (Log-Channel setzen/erstellen) und aktivieren\n" +
                    "`!disable` - Security deaktivieren\n" +
                    "`!status` - Security Status anzeigen (enabled, logchannel, timeout settings, etc.)\n\n" +
                    "**Cleanup (Channel)**\n" +
                    "`!cleanup-now` - löscht Nachrichten im aktuellen Channel\n" +
                    "`!cleanup-intervall <channelId> [interval]` - Auto-Cleanup Intervall setzen (Aliases: !setcleanupinterval)\n" +
                    "→ Beispiele: 1h, 10h, 5d\n" +
                    "`!delcleanup-intervall <channelId>` - Auto-Cleanup Intervall entfernen (Aliases: !cleanupdel, !removecleanupinterval)\n\n" +
                    "**Danger Zone (Owner-only + confirmation)**\n" +
                    "`!cleanup-server-ultra` - löscht ALLE Channels + Kategorien (nur Owner + Bestätigungsphrase)", false)

                .AddField("★ **Ticket System**",
                    "`!ticket-setup` - Interactive setup: Configure ticket system\n" +
                    "`!set-support [ROLE_ID]` - Set support role for ticket management\n" +
                    "`!ticket-close` - Close the current ticket\n" +
                    "`!ticket-status` - Show ticket system status\n" +
                    "`!ticket-transcript` - Generate transcript for ticket\n" +
                    "`!del-ticket-system` - Remove ticket system configuration", false)

                .AddField("★ **Voice Features**",
                    "`!voicesetup [log-channel-id] [voice-category-id] [role-id]` - Complete voice system setup\n" +
                    "`!create [category-id]` - Create Join-to-Create channel\n" +
                    "`!voice-cleanup` - Clean up all empty voice channels (Admin)\n" +
                    "`!remove-voice-setup` - Disable voice system (keeps channels)\n" +
                    "`!voicename [name]` - Rename your voice channel\n" +
                    "`!voicelimit [0-99]` - Set user limit (0=unlimited)\n" +
                    "`!voiceprivate` - Make your channel private\n" +
                    "`!voicepublic` - Make your channel public\n" +
                    "`!voicetemplate [name]` - Save/load voice channel template\n" +
                    "`!voicestats` - View voice activity statistics\n" +
                    "`!voicehelp` - Detailed voice system help", false)

                .AddField("★ **XP & Level System**",
                    "`!run-xp-setup` - XP-System einrichten (erstellt alle Rollen)\n" +
                    "`!remove-xp-setup` - XP-System entfernen\n" +
                    "`!xp [user]` - XP-Status anzeigen\n" +
                    "`!xp-give @user <amount>` - **ADMIN:** Grant XP to user (max 1M)\n" +
                    "`!set-rank-channel #channel` - Level-Up Nachrichten Channel setzen\n" +
                    "`!rank-channel-off` - Level-Up Nachrichten deaktivieren\n" +
                    "`!rank-channel-status` - Status des Rank-Channels anzeigen\n" +
                    "→ Sammle XP durch Chatten und Voice-Aktivität!", false)

                .AddField("★ **Giveaway System**",
                    "`!gcreate <Zeit> <Gewinner> <Preis>` - Erstelle ein Giveaway (z.B. !gcreate 1h 1 Discord Nitro)\n" +
                    "`!gend <Message ID>` - Beende Giveaway vorzeitig\n" +
                    "`!greroll <Message ID>` - Wähle neue Gewinner\n" +
                    "`!glist` - Zeige alle aktiven Giveaways\n" +
                    "`!ghelp` - Detaillierte Giveaway-Hilfe\n" +
                    "→ Reagiere mit 🎉 um an Giveaways teilzunehmen!", false)

                .AddField("★ **Bump Reminders**",
                    "`!bumpreminder on/off` - Enable/disable bump reminders\n" +
                    "`!bumpstatus` - Show bump reminder status\n" +
                    "→ Automatically reminds you every 2 hours when server can be bumped\n" +
                    "→ Works with Disboard bot for server promotion", false)

                .AddField("★ **Utilities**",
                    "`!sendit MESSAGE_ID to CHANNEL_ID` - Forward a message to another channel", false)

                .AddField("★ **Birthday System**",
                    "`!birthdaychannel [CHANNEL_ID or !new-birthchan]` - Set/create birthday channel *(Admin)*\n" +
                    "`!birthdayset [dd.mm.yyyy]` - Save your birthday (format: 25.12.1995)\n" +
                    "`!birthdayremove` / `!mybirthdayremove` - Remove your birthday\n" +
                    "`!mybirthdayinfo` - View your saved birthday\n" +
                    "`!birthdaylist` - View all birthdays sorted by date *(Admin)*", false)

                .AddField("★ **Economy System**",
                    "`!daily` - Collect your daily credits\n" +
                    "`!balance` / `!bal` - Check wallet + bank balance\n" +
                    "`!deposit <amount>` / `!dep all` - Deposit money safely in bank\n" +
                    "`!withdraw <amount>` / `!with all` - Withdraw from bank\n" +
                    "`!pay @user <amount>` - Send money to another user\n" +
                    "`!rob @user` - Try to steal from wallet (risky! 40% success rate)\n" +
                    "→ *Bank money is SAFE from robbers!*", false)

                .AddField("★ **Casino & Gambling**",
                    "`!coinflip <bet> <heads/tails>` - Coin flip with animation\n" +
                    "`!dice <bet> <prediction> [6/20/100]` - Multi-sided dice casino\n" +
                    "`!slots <bet>` - Slot machine with jackpot chance\n" +
                    "→ *Anti-addiction smart betting limits built-in*", false)

                .WithImageUrl("https://imgur.com/aYh8OAq.png")
                .WithFooter("Code.Master() BOT - THE ULTIMATE FREE EXPERIENCE! DESTROYS NOCTALY, ZENIA VOICE, SAPPHIRE & TICKET KING", "https://i.imgur.com/7mkVUuO.png")
                .WithCurrentTimestamp();

            await ReplyAsync(embed: embed.Build());
        }

        [Command("info")]
        [Summary("Shows bot information")]
        public async Task InfoAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("Bot Information")
                .WithColor(Color.Green)
                .AddField("Bot Name", Context.Client.CurrentUser.Username, true)
                .AddField("Server", "[Click me <3](https://discord.gg/hQmvHTs9vz)", true)
                .AddField("Online since", Context.Client.CurrentUser.CreatedAt.ToString("dd.MM.yyyy HH:mm"), true)
                .AddField("Latency", $"{Context.Client.Latency}ms", true)
                .AddField("Framework", "Discord.NET", true)
                .AddField("Language", "C#", true)
                .WithThumbnailUrl(Context.Client.CurrentUser.GetAvatarUrl())
                .WithFooter("Made with ❤️ from mgb")
                .WithCurrentTimestamp();

            await ReplyAsync(embed: embed.Build());
        }

        [Command("gn")]
        [Summary("Good night message")]
        public async Task GoodNightAsync()
        {
            var messages = new[]
            {
                "Good night! 🌙 Sleep well!",
                "Sweet dreams! 😴💤",
                "Sleep tight! 🌙✨",
                "Good night and restful sleep! 😌",
                "Dream something beautiful! 🌙💫",
                "Nighty night! Don't let the bedbugs bite! 🛏️",
                "Rest well, you earned it! 💪🌙",
                "Time to recharge! See you tomorrow! 🔋✨",
                "Off to dreamland! Safe travels! 🌠",
                "May your pillow be soft and your dreams be sweet! 😊",
                "Catch you on the flip side! Sleep tight! 🌙",
                "Lights out! Time for some quality Zzz's! 💡😴",
                "Pleasant dreams and peaceful sleep! 🌌",
                "Good night! Hope you wake up refreshed! ☀️",
                "Sleep well, friend! Tomorrow's a new adventure! 🎒",
                "Night night! Don't stay up too late! ⏰",
                "Time to hit the hay! Good night! 🌾",
                "Sweet slumber awaits! Rest easy! 😌💤",
                "Good night! May your dreams be as awesome as you! 🌟",
                "Off to bed! See you in the morning sunshine! 🌅"
            };

            var random = new Random();
            var message = messages[random.Next(messages.Length)];

            await ReplyAsync(message);
        }

        [Command("hi")]
        [Summary("Say hello")]
        public async Task HelloAsync()
        {
            var messages = new[]
            {
                "Hey there! 👋 How's it going?",
                "Hello! 😊 Great to see you!",
                "Hi! 🎉 Welcome back!",
                "Hey! 👋 What's up?",
                "Howdy! 🤠 Nice to have you here!",
                "Hiya! 😄 How are you doing?",
                "Hello there! 🌟 Good to see you!",
                "Hey hey! 👋 What brings you here today?",
                "Hi there! 😊 Hope you're having a great day!",
                "Greetings! 🎊 How can I help?",
                "Yo! 🤘 What's happening?",
                "Hello friend! 👋 How's your day going?",
                "Hey! 😁 Long time no see!",
                "Hi! ✨ Ready for some fun?",
                "Hello! 🌈 Lovely to see you!",
                "Hey there! 🎮 What's new?",
                "Hi! 🚀 Hope you're doing awesome!",
                "Hello! 💬 Feel free to chat!",
                "Hey! 🎵 How's everything with you?",
                "Hi there! 🌻 Have a wonderful day!"
            };

            var random = new Random();
            var message = messages[random.Next(messages.Length)];

            await ReplyAsync(message);
        }

        // SPECIAL COMMAND TO SHOW NOCTALY WHO'S BOSS!
        [Command("vs-noctaly")]
        [Alias("better-than-noctaly", "noctaly-sucks")]
        [Summary("See how C-MAIN destroys Noctaly in every way!")]
        public async Task VsNoctalyAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("🚀 C-MAIN vs Noctaly - NO CONTEST! 🚀")
                .WithDescription("**Why C-MAIN DESTROYS Noctaly:**\n\n")
                .AddField("🎰 **Gaming System**",
                    "**C-MAIN:** Advanced casino with coinflip, dice, slots + animations\n" +
                    "**Noctaly:** Basic coinflip only\n" +
                    "🏆 **WINNER: C-MAIN** (100x better!)", false)
                .AddField("💰 **Economy Features**", 
                    "**C-MAIN:** Work system, daily rewards, levels, XP, streaks\n" +
                    "**Noctaly:** Basic work/daily only\n" +
                    "🏆 **WINNER: C-MAIN** (Infinitely better!)", false)
                .AddField("📊 **Rankings & Stats**",
                    "**C-MAIN:** Multiple leaderboards, detailed profiles, personal ranks\n" +
                    "**Noctaly:** Basic leaderboard\n" +
                    "🏆 **WINNER: C-MAIN** (Way more advanced!)", false)
                .AddField("🛡️ **Security & Features**",
                    "**C-MAIN:** Advanced security, tickets, voice, birthdays, bump reminders\n" +
                    "**Noctaly:** Setup/Premium only\n" +
                    "🏆 **WINNER: C-MAIN** (More features!)", false)
                .AddField("🎨 **Design & UX**",
                    "**C-MAIN:** Beautiful embeds, animations, interactive help\n" +
                    "**Noctaly:** Basic text responses\n" +
                    "🏆 **WINNER: C-MAIN** (Professional quality!)", false)
                .AddField("🔥 **THE VERDICT**",
                    "**C-MAIN ABSOLUTELY DESTROYS NOCTALY!**\n\n" +
                    "✅ More features\n" +
                    "✅ Better design\n" +
                    "✅ Superior performance\n" +
                    "✅ More fun\n" +
                    "✅ Actually innovative\n\n" +
                    "💪 **C-MAIN is the FUTURE of Discord bots!**", false)
                .WithColor(0x00FF00)
                .WithFooter("Noctaly could never compete! 💀")
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("gm")]
        [Summary("Good morning message")]
        public async Task GoodMorningAsync()
        {
            var messages = new[]
            {
                "Good morning! ☀️ Have a beautiful day!",
                "Morning! 🌅 Did you sleep well?",
                "Good morning! ☕ Ready for a new day?",
                "Morning! 🌞 Hope you're feeling good!",
                "Good morning! 🌻 Let's rock this day!",
                "Rise and shine! ✨ Time to conquer the day!",
                "Good morning! 🌈 Make today amazing!",
                "Morning sunshine! ☀️ Ready to crush it?",
                "Top of the morning to you! 🎩 Let's go!",
                "Good morning! 🚀 Today's full of possibilities!",
                "Wake up and be awesome! 💪 Good morning!",
                "Morning! 🌄 Hope you slept like a baby!",
                "Good morning! 🎊 Time to make magic happen!",
                "Rise and grind! ⚡ Good morning!",
                "Morning! 🦅 Soar high today!",
                "Good morning! 🌺 Wishing you a fantastic day!",
                "Wakey wakey! 🥞 Time for some breakfast!",
                "Good morning! 🎯 Let's hit those goals!",
                "Morning! 🌟 Shine bright today!",
                "Good morning! 🎮 Ready to level up your day?"
            };

            var random = new Random();
            var message = messages[random.Next(messages.Length)];

            await ReplyAsync(message);
        }

        [Command("bumpreminder")]
        [Summary("Toggle bump reminders")]
        public async Task BumpReminderAsync(string? action = null)
        {
            if (action == null)
            {
                var usageEmbed = new EmbedBuilder()
                    .WithColor(0xFFD700) // Gold/Yellow
                    .WithTitle("📝 !bumpreminder Command Usage")
                    .WithDescription("Enable or disable automatic bump reminders for your server.")
                    .AddField("Usage", "`!bumpreminder <on|off>`", false)
                    .AddField("Example", "`!bumpreminder on` - Enable reminders\n`!bumpreminder off` - Disable reminders", false)
                    .WithFooter("You need Administrator permissions to use this command")
                    .Build();

                await ReplyAsync(embed: usageEmbed);
                return;
            }

            if (action.ToLower() == "on")
            {
                BumpReminderService.SetBumpReminder(Context.Guild.Id, Context.Channel.Id);
                var onEmbed = new EmbedBuilder()
                    .WithTitle("✅ Bump Reminders Activated")
                    .WithDescription("Bump reminders have been activated! You will be notified when the server can be bumped again.")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: onEmbed);
            }
            else if (action.ToLower() == "off")
            {
                BumpReminderService.RemoveBumpReminder(Context.Guild.Id);
                var offEmbed = new EmbedBuilder()
                    .WithTitle("❌ Bump Reminders Deactivated")
                    .WithDescription("Bump reminders have been deactivated.")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: offEmbed);
            }
            else
            {
                var invalidEmbed = new EmbedBuilder()
                    .WithTitle("❌ Invalid Option")
                    .WithDescription("Invalid option. Use `on` or `off`.")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: invalidEmbed);
            }
        }

        [Command("bumpstatus")]
        [Summary("Check bump reminder status")]
        public async Task BumpStatusAsync()
        {
            var status = BumpReminderService.GetBumpReminderStatus(Context.Guild.Id);

            var embed = new EmbedBuilder()
                .WithTitle("Bump Reminder Status")
                .WithColor(status?.IsActive == true ? Color.Green : Color.Red)
                .AddField("Status", status?.IsActive == true ? "✅ Enabled" : "❌ Disabled", true)
                .AddField("Channel", Context.Channel.Name, true);

            if (status?.IsActive == true && status.NextBumpTime > DateTime.Now)
            {
                var timeRemaining = status.NextBumpTime - DateTime.Now;
                if (timeRemaining.TotalSeconds > 0)
                {
                    embed.AddField("Next bump possible in",
                        $"{timeRemaining.Hours}h {timeRemaining.Minutes}m {timeRemaining.Seconds}s", false);
                }
                else
                {
                    embed.AddField("Next Bump", "Now possible! 🎉", false);
                }
            }

            embed.WithCurrentTimestamp();
            await ReplyAsync(embed: embed.Build());
        }

        [Command("sendit")]
        [Summary("Forward a message to another channel")]
        public async Task SendItAsync([Remainder] string? args = null)
        {
            // Check admin permissions
            var guildUser = Context.Guild.GetUser(Context.User.Id);
            if (guildUser == null || !guildUser.GuildPermissions.Has(GuildPermission.Administrator))
            {
                await ReplyAsync("❌ This command requires Administrator permissions.");
                return;
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                var usageEmbed = new EmbedBuilder()
                    .WithColor(0xFFD700) // Gold/Yellow
                    .WithTitle("📝 !sendit Command Usage")
                    .WithDescription("Forward a message from this channel to another channel.")
                    .AddField("Usage", "`!sendit MESSAGE_ID to CHANNEL_ID`", false)
                    .AddField("Example", "`!sendit 1234567890123456789 to 9876543210987654321`", false)
                    .WithFooter("You need Administrator permissions to use this command")
                    .Build();

                await ReplyAsync(embed: usageEmbed);
                return;
            }

            // Parse: MESSAGE_ID to CHANNEL_ID
            var parts = args.Split(new[] { " to " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                await ReplyAsync("❌ Invalid format! Use: `!sendit MESSAGE_ID to CHANNEL_ID`");
                return;
            }

            var messageIdStr = parts[0].Trim();
            var channelIdStr = parts[1].Trim();

            if (!ulong.TryParse(messageIdStr, out var messageId))
            {
                await ReplyAsync("❌ Invalid MESSAGE_ID! Must be a valid snowflake ID.");
                return;
            }

            if (!ulong.TryParse(channelIdStr, out var channelId))
            {
                await ReplyAsync("❌ Invalid CHANNEL_ID! Must be a valid snowflake ID.");
                return;
            }

            try
            {
                // Fetch the message from current channel
                var originalMessage = await Context.Channel.GetMessageAsync(messageId);
                if (originalMessage == null)
                {
                    var notFoundEmbed = new EmbedBuilder()
                        .WithTitle("❌ Message Not Found")
                        .WithDescription("Message not found in this channel!")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: notFoundEmbed);
                    return;
                }

                // Get target channel
                var targetChannel = Context.Guild.GetTextChannel(channelId);
                if (targetChannel == null)
                {
                    var chanNotFoundEmbed = new EmbedBuilder()
                        .WithTitle("❌ Channel Not Found")
                        .WithDescription("Target channel not found or not accessible!")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: chanNotFoundEmbed);
                    return;
                }

                // Check bot permissions in target channel
                var botPerms = targetChannel.GetPermissionOverwrite(Context.Guild.CurrentUser);
                if (botPerms?.SendMessages == PermValue.Deny)
                {
                    await ReplyAsync($"❌ I don't have permission to send messages in {targetChannel.Mention}!");
                    return;
                }

                // Forward only the message content - no embeds, no author info
                var content = originalMessage.Content;
                
                // If message has content, send it
                if (!string.IsNullOrWhiteSpace(content))
                {
                    await targetChannel.SendMessageAsync(content);
                }
                
                // Forward attachments if any
                if (originalMessage.Attachments.Count > 0)
                {
                    foreach (var attachment in originalMessage.Attachments)
                    {
                        await targetChannel.SendMessageAsync(attachment.Url);
                    }
                }

                await ReplyAsync($"✅ Message forwarded to {targetChannel.Mention}!");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Error forwarding message: {ex.Message}");
            }
        }

        // ECONOMY SHOWCASE COMMAND
        [Command("economy-help")]
        [Alias("eco-help", "money-help")]
        [Summary("Detailed economy system help - Way better than other bots!")]
        public async Task EconomyHelpAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("💰 Code.Master() - Economy System 💰")
                .AddField("**Getting Started**",
                    "`!daily` - Claim daily rewards (up to 2500+ credits!)\n" +
                    "`!work` - Work at 10+ different jobs\n" +
                    "`!balance` - Check your wallet & bank balance\n" +
                    "`!profile` - View complete user profile", false)
                .AddField("**💰 Banking System (NEW!)**",
                    "`!deposit <amount>` / `!dep all` - Store money safely in bank\n" +
                    "`!withdraw <amount>` / `!with all` - Get money from bank\n" +
                    "→ **Bank money is SAFE from robberies!**\n" +
                    "→ **Strategic money management!**", false)
                .AddField("**🔫 Robbery & Trading (NEW!)**",
                    "`!rob @user` - Try to steal from their wallet (risky!)\n" +
                    "`!pay @user <amount>` - Send money to another user\n" +
                    "→ **40% success rate + 2% per level**\n" +
                    "→ **Lose money if robbery fails!**\n" +
                    "→ **1 hour cooldown between attempts**", false)
                .AddField("**Gaming & Gambling**",
                    "`!coinflip <bet> <heads/tails>` - Smart coin betting\n" +
                    "`!dice <bet> <prediction> [6/20/100]` - Multi-dice casino\n" +
                    "`!slots <bet>` - Mega slot machine\n" +
                    "→ **Anti-addiction protection built-in!**", false)
                .AddField("**Rankings & Competition**",
                    "`!rank` - Your personal server ranking\n" +
                    "`!leaderboard money` - Richest users\n" +
                    "`!leaderboard level` - Highest levels\n" +
                    "→ **Multiple leaderboard categories!**", false)
                .AddField("**Level System & Rewards**",
                    "• Gain XP from earning money\n" +
                    "• Level up for better job opportunities\n" +
                    "• Level bonuses on work & daily\n" +
                    "• Higher level = better robbery success rate\n" +
                    "• Show off your progress!", false)
                .WithColor(0xFFD700)
                .WithFooter("Build with VS Code of mungabee/ozzygirl")
                .Build();

            await ReplyAsync(embed: embed);
        }
    }
}
