using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using MainbotCSharp.Services;

namespace MainbotCSharp.Modules
{
    public class GiveawayCommands : ModuleBase<SocketCommandContext>
    {
        private readonly GiveawayService _giveawayService;

        public GiveawayCommands(GiveawayService giveawayService)
        {
            _giveawayService = giveawayService;
        }

        [Command("gcreate")]
        [Alias("giveaway-create", "gstart")]
        [Summary("Erstellt ein Giveaway. Format: !gcreate <Zeit> <Gewinner> <Preis>")]
        [RequireManageMessagesOrBotOwner]
        public async Task CreateGiveawayAsync([Remainder] string parameters = "")
        {
            if (string.IsNullOrWhiteSpace(parameters))
            {
                var helpEmbed = new EmbedBuilder()
                    .WithTitle("🎉 Giveaway erstellen")
                    .WithDescription("**Verwendung:** `!gcreate <Zeit> <Gewinner> <Preis>`\n\n" +
                                   "**Zeit-Formate:**\n" +
                                   "• `10m` = 10 Minuten\n" +
                                   "• `2h` = 2 Stunden\n" +
                                   "• `1d` = 1 Tag\n" +
                                   "• `1w` = 1 Woche\n\n" +
                                   "**Beispiele:**\n" +
                                   "• `!gcreate 1h 1 Discord Nitro`\n" +
                                   "• `!gcreate 30m 3 Steam Keys`\n" +
                                   "• `!gcreate 1d 2 100€ Amazon Gutschein`")
                    .WithColor(Color.Blue)
                    .Build();

                await ReplyAsync(embed: helpEmbed);
                return;
            }

            // Parse parameters: <time> <winners> <prize>
            var parts = parameters.Split(' ', 3);
            if (parts.Length < 3)
            {
                await ReplyAsync("❌ **Fehler:** Nicht genügend Parameter!\n" +
                               "**Format:** `!gcreate <Zeit> <Gewinner> <Preis>`\n" +
                               "**Beispiel:** `!gcreate 1h 1 Discord Nitro`");
                return;
            }

            // Parse time
            var timeStr = parts[0];
            if (!TryParseTime(timeStr, out var duration))
            {
                await ReplyAsync("❌ **Fehler:** Ungültiges Zeitformat!\n" +
                               "**Erlaubte Formate:** `10m`, `2h`, `1d`, `1w`\n" +
                               "**Beispiel:** `1h` für 1 Stunde");
                return;
            }

            // Parse winner count
            var winnerStr = parts[1];
            if (!int.TryParse(winnerStr, out var winners) || winners < 1 || winners > 20)
            {
                await ReplyAsync("❌ **Fehler:** Ungültige Gewinnerzahl!\n" +
                               "**Erlaubt:** 1-20 Gewinner\n" +
                               "**Beispiel:** `1` für einen Gewinner");
                return;
            }

            // Get prize
            var prize = parts[2];
            if (string.IsNullOrWhiteSpace(prize))
            {
                await ReplyAsync("❌ **Fehler:** Kein Preis angegeben!\n" +
                               "**Beispiel:** `Discord Nitro` oder `100€ Amazon Gutschein`");
                return;
            }

            // Validate minimum time (1 minute)
            if (duration.TotalMinutes < 1)
            {
                await ReplyAsync("❌ **Fehler:** Giveaway muss mindestens 1 Minute dauern!");
                return;
            }

            // Validate maximum time (1 month)
            if (duration.TotalDays > 31)
            {
                await ReplyAsync("❌ **Fehler:** Giveaway kann maximal 31 Tage dauern!");
                return;
            }

            // Create giveaway
            var success = await _giveawayService.CreateGiveaway(Context.Channel, prize, duration, winners, Context.User);
            
            if (success)
            {
                // Delete the command message
                try
                {
                    await Context.Message.DeleteAsync();
                }
                catch { /* Ignore if can't delete */ }

                // Send confirmation DM
                try
                {
                    var dmEmbed = new EmbedBuilder()
                        .WithTitle("✅ Giveaway erstellt!")
                        .WithDescription($"**Preis:** {prize}\n" +
                                       $"**Dauer:** {FormatDuration(duration)}\n" +
                                       $"**Gewinner:** {winners}\n" +
                                       $"**Kanal:** {Context.Channel.Name}")
                        .WithColor(Color.Green)
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    await Context.User.SendMessageAsync(embed: dmEmbed);
                }
                catch { /* Ignore if can't send DM */ }
            }
            else
            {
                await ReplyAsync("❌ **Fehler:** Giveaway konnte nicht erstellt werden!");
            }
        }

        [Command("gend")]
        [Alias("giveaway-end")]
        [Summary("Beendet ein Giveaway vorzeitig")]
        [RequireManageMessagesOrBotOwner]
        public async Task EndGiveawayAsync(ulong messageId = 0)
        {
            if (messageId == 0)
            {
                await ReplyAsync("❌ **Fehler:** Bitte gib die Message ID des Giveaways an!\n" +
                               "**Verwendung:** `!gend <Message ID>`\n" +
                               "**Tipp:** Rechtsklick auf die Giveaway-Nachricht → \"ID kopieren\"");
                return;
            }

            var success = await _giveawayService.EndGiveaway(messageId);
            
            if (success)
            {
                await ReplyAsync("✅ Giveaway wurde erfolgreich beendet!");
            }
            else
            {
                await ReplyAsync("❌ **Fehler:** Giveaway nicht gefunden oder bereits beendet!\n" +
                               "**Hinweis:** Stelle sicher, dass die Message ID korrekt ist und das Giveaway aktiv ist.");
            }
        }

        [Command("greroll")]
        [Alias("giveaway-reroll")]
        [Summary("Wählt neue Gewinner für ein beendetes Giveaway")]
        [RequireManageMessagesOrBotOwner]
        public async Task RerollGiveawayAsync(ulong messageId = 0)
        {
            if (messageId == 0)
            {
                await ReplyAsync("❌ **Fehler:** Bitte gib die Message ID des Giveaways an!\n" +
                               "**Verwendung:** `!greroll <Message ID>`\n" +
                               "**Tipp:** Rechtsklick auf die Giveaway-Nachricht → \"ID kopieren\"");
                return;
            }

            var success = await _giveawayService.RerollGiveaway(messageId);
            
            if (success)
            {
                await ReplyAsync("🎲 Neue Gewinner wurden ausgewählt!");
            }
            else
            {
                await ReplyAsync("❌ **Fehler:** Giveaway nicht gefunden!\n" +
                               "**Hinweis:** Stelle sicher, dass die Message ID korrekt ist.");
            }
        }

        [Command("glist")]
        [Alias("giveaway-list", "giveaways")]
        [Summary("Zeigt alle aktiven Giveaways")]
        public async Task ListGiveawaysAsync()
        {
            var activeGiveaways = _giveawayService.GetActiveGiveaways();
            
            if (!activeGiveaways.Any())
            {
                await ReplyAsync("📋 **Keine aktiven Giveaways** gefunden!");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("🎉 Aktive Giveaways")
                .WithColor(Color.Gold)
                .WithFooter($"Insgesamt {activeGiveaways.Count} aktive(s) Giveaway(s)")
                .WithTimestamp(DateTimeOffset.UtcNow);

            foreach (var giveaway in activeGiveaways.Take(10)) // Limit to 10 to avoid embed limits
            {
                var channel = Context.Client.GetChannel(giveaway.ChannelId) as ISocketMessageChannel;
                var timeLeft = giveaway.EndTime - DateTime.UtcNow;
                var timeText = FormatTimeLeft(timeLeft);

                embed.AddField($"🎁 {giveaway.Prize}",
                             $"**Kanal:** {channel?.Name ?? "Unbekannt"}\n" +
                             $"**Endet in:** {timeText}\n" +
                             $"**Gewinner:** {giveaway.WinnerCount}\n" +
                             $"**Message ID:** `{giveaway.MessageId}`",
                             true);
            }

            if (activeGiveaways.Count > 10)
            {
                embed.WithDescription($"*Zeige die ersten 10 von {activeGiveaways.Count} Giveaways*");
            }

            await ReplyAsync(embed: embed.Build());
        }

        [Command("ghelp")]
        [Alias("giveaway-help")]
        [Summary("Zeigt die Giveaway-Hilfe")]
        public async Task GiveawayHelpAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("🎉 Giveaway Bot Hilfe")
                .WithDescription("**Alle Giveaway-Befehle mit dem ! Prefix**")
                .WithColor(Color.Blue)
                .WithThumbnailUrl(Context.Client.CurrentUser.GetAvatarUrl())
                
                .AddField("📝 **Giveaway erstellen**",
                         "`!gcreate <Zeit> <Gewinner> <Preis>`\n" +
                         "**Beispiel:** `!gcreate 1h 1 Discord Nitro`\n" +
                         "*Benötigt: Nachrichten verwalten*", false)
                
                .AddField("⏹️ **Giveaway beenden**",
                         "`!gend <Message ID>`\n" +
                         "**Beispiel:** `!gend 1234567890123456789`\n" +
                         "*Benötigt: Nachrichten verwalten*", false)
                
                .AddField("🎲 **Neue Gewinner wählen**",
                         "`!greroll <Message ID>`\n" +
                         "**Beispiel:** `!greroll 1234567890123456789`\n" +
                         "*Benötigt: Nachrichten verwalten*", false)
                
                .AddField("📋 **Aktive Giveaways anzeigen**",
                         "`!glist` oder `!giveaways`\n" +
                         "Zeigt alle laufenden Giveaways", false)
                
                .AddField("ℹ️ **Hilfe anzeigen**",
                         "`!ghelp`\n" +
                         "Zeigt diese Hilfe-Nachricht", false)
                
                .AddField("⏰ **Zeit-Formate**",
                         "`10m` = 10 Minuten\n" +
                         "`2h` = 2 Stunden\n" +
                         "`1d` = 1 Tag\n" +
                         "`1w` = 1 Woche", true)
                
                .AddField("🎯 **Teilnahme**",
                         "Reagiere mit 🎉 auf ein Giveaway um teilzunehmen!\n" +
                         "Entferne deine Reaktion um dich abzumelden.", true)
                
                .WithFooter("Code.Master() Giveaway System | Alle Features kostenlos!")
                .WithTimestamp(DateTimeOffset.UtcNow);

            await ReplyAsync(embed: embed.Build());
        }

        private static bool TryParseTime(string timeStr, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            
            if (string.IsNullOrWhiteSpace(timeStr))
                return false;

            var match = Regex.Match(timeStr.ToLower(), @"^(\d+)(m|h|d|w)$");
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups[1].Value, out var value) || value <= 0)
                return false;

            var unit = match.Groups[2].Value;
            duration = unit switch
            {
                "m" => TimeSpan.FromMinutes(value),
                "h" => TimeSpan.FromHours(value),
                "d" => TimeSpan.FromDays(value),
                "w" => TimeSpan.FromDays(value * 7),
                _ => TimeSpan.Zero
            };

            return duration > TimeSpan.Zero;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 7)
                return $"{(int)(duration.TotalDays / 7)} Woche(n)";
            if (duration.TotalDays >= 1)
                return $"{(int)duration.TotalDays} Tag(e)";
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours} Stunde(n)";
            return $"{(int)duration.TotalMinutes} Minute(n)";
        }

        private static string FormatTimeLeft(TimeSpan timeLeft)
        {
            if (timeLeft.TotalSeconds <= 0)
                return "**Beendet**";
            
            if (timeLeft.TotalDays >= 1)
                return $"{(int)timeLeft.TotalDays}d {timeLeft.Hours}h {timeLeft.Minutes}m";
            if (timeLeft.TotalHours >= 1)
                return $"{timeLeft.Hours}h {timeLeft.Minutes}m";
            return $"{timeLeft.Minutes}m";
        }
    }
}