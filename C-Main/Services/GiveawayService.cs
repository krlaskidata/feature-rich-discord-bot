using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Discord;
using Discord.WebSocket;
using System.IO;
using System.Text.Json;

namespace MainbotCSharp.Services
{
    public class GiveawayService
    {
        private readonly DiscordSocketClient _client;
        private readonly Dictionary<ulong, GiveawayData> _activeGiveaways;
        private readonly System.Timers.Timer _checkTimer;
        private readonly string _dataFilePath;

        public GiveawayService(DiscordSocketClient client)
        {
            _client = client;
            _activeGiveaways = new Dictionary<ulong, GiveawayData>();
            _dataFilePath = Path.Combine(Directory.GetCurrentDirectory(), "giveaway_data.json");

            _checkTimer = new System.Timers.Timer(30000);
            _checkTimer.Elapsed += CheckEndedGiveaways;
            _checkTimer.AutoReset = true;
            _checkTimer.Start();

            LoadGiveaways();
            _client.ReactionAdded += HandleReactionAdded;
            _client.ReactionRemoved += HandleReactionRemoved;
        }

        private async void CheckEndedGiveaways(object? sender, ElapsedEventArgs e)
        {
            var now = DateTime.UtcNow;
            var endedGiveaways = _activeGiveaways.Values.Where(g => g.EndTime <= now).ToList();

            foreach (var giveaway in endedGiveaways)
            {
                await EndGiveaway(giveaway.MessageId);
            }
        }

        public async Task<bool> CreateGiveaway(ISocketMessageChannel channel, string prize, TimeSpan duration, int winners, IUser host)
        {
            try
            {
                var endTime = DateTime.UtcNow.Add(duration);
                var embed = CreateGiveawayEmbed(prize, endTime, winners, host, 0);

                var message = await channel.SendMessageAsync(embed: embed);
                await message.AddReactionAsync(new Emoji("🎉"));

                var giveawayData = new GiveawayData
                {
                    MessageId = message.Id,
                    ChannelId = channel.Id,
                    Prize = prize,
                    EndTime = endTime,
                    WinnerCount = winners,
                    HostId = host.Id,
                    Participants = new List<ulong>()
                };

                _activeGiveaways[message.Id] = giveawayData;
                SaveGiveaways();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating giveaway: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EndGiveaway(ulong messageId, bool isReroll = false)
        {
            if (!_activeGiveaways.TryGetValue(messageId, out var giveaway))
                return false;

            try
            {
                var channel = _client.GetChannel(giveaway.ChannelId) as ISocketMessageChannel;
                if (channel == null) return false;

                var message = await channel.GetMessageAsync(messageId) as IUserMessage;
                if (message == null) return false;


                var reaction = message.Reactions.FirstOrDefault(r => r.Key.Name == "🎉");
                if (reaction.Key != null)
                {
                    var users = await message.GetReactionUsersAsync(reaction.Key, reaction.Value.ReactionCount).FlattenAsync();
                    giveaway.Participants = users.Where(u => !u.IsBot).Select(u => u.Id).ToList();
                }

                List<IUser> winners = new List<IUser>();
                
                if (giveaway.Participants.Count == 0)
                {

                    var embed = new EmbedBuilder()
                        .WithTitle($"🎉 {giveaway.Prize} 🎉")
                        .WithDescription("**Giveaway beendet!**\n\nKeine Teilnehmer gefunden! 😢")
                        .WithColor(Color.Red)
                        .WithFooter($"Beendet um {DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC")
                        .Build();

                    await message.ModifyAsync(m => m.Embed = embed);
                }
                else
                {

                    var random = new Random();
                    var winnerIds = giveaway.Participants
                        .OrderBy(x => random.Next())
                        .Take(giveaway.WinnerCount)
                        .ToList();

                    foreach (var winnerId in winnerIds)
                    {
                        var winner = _client.GetUser(winnerId);
                        if (winner != null)
                            winners.Add(winner);
                    }

                    string winnerText;
                    if (winners.Count == 0)
                    {
                        winnerText = "Keine gültigen Gewinner gefunden!";
                    }
                    else if (winners.Count == 1)
                    {
                        winnerText = $"🎊 **Gewinner:** {winners[0].Mention}\n\nHerzlichen Glückwunsch! 🎉";
                    }
                    else
                    {
                        winnerText = $"🎊 **Gewinner:**\n{string.Join("\n", winners.Select(w => $"• {w.Mention}"))}\n\nHerzlichen Glückwunsch! 🎉";
                    }

                    var embed = new EmbedBuilder()
                        .WithTitle($"🎉 {giveaway.Prize} 🎉")
                        .WithDescription($"**Giveaway beendet!**\n\n{winnerText}")
                        .WithColor(Color.Green)
                        .WithFooter($"Beendet um {DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC | {giveaway.Participants.Count} Teilnehmer")
                        .Build();

                    await message.ModifyAsync(m => m.Embed = embed);

                    if (winners.Count > 0)
                    {
                        var congratsMessage = winners.Count == 1 
                            ? $"🎉 Herzlichen Glückwunsch {winners[0].Mention}! Du hast **{giveaway.Prize}** gewonnen!"
                            : $"🎉 Herzlichen Glückwunsch {string.Join(", ", winners.Select(w => w.Mention))}! Ihr habt **{giveaway.Prize}** gewonnen!";
                        
                        await channel.SendMessageAsync(congratsMessage);
                    }
                }

                if (!isReroll)
                {
                    _activeGiveaways.Remove(messageId);
                    SaveGiveaways();
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ending giveaway: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RerollGiveaway(ulong messageId)
        {
            if (!_activeGiveaways.TryGetValue(messageId, out var giveaway))
            {

                return await EndGiveaway(messageId, true);
            }

            return await EndGiveaway(messageId, true);
        }

        private async Task HandleReactionAdded(Cacheable<IUserMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel, SocketReaction reaction)
        {
            if (reaction.Emote.Name != "🎉" || reaction.User.Value?.IsBot == true)
                return;

            if (_activeGiveaways.ContainsKey(cachedMessage.Id))
            {
                // Update participant count display immediately
                await UpdateGiveawayDisplay(cachedMessage.Id);
                Console.WriteLine($"Giveaway {cachedMessage.Id}: Teilnehmer hinzugefügt - {reaction.User.Value?.Username}");
            }
        }

        private async Task HandleReactionRemoved(Cacheable<IUserMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel, SocketReaction reaction)
        {
            if (reaction.Emote.Name != "🎉" || reaction.User.Value?.IsBot == true)
                return;

            if (_activeGiveaways.ContainsKey(cachedMessage.Id))
            {
                // Update participant count display immediately
                await UpdateGiveawayDisplay(cachedMessage.Id);
                Console.WriteLine($"Giveaway {cachedMessage.Id}: Teilnehmer entfernt - {reaction.User.Value?.Username}");
            }
        }

        private async Task UpdateGiveawayDisplay(ulong messageId)
        {
            try
            {
                if (!_activeGiveaways.TryGetValue(messageId, out var giveaway))
                    return;

                var channel = _client.GetChannel(giveaway.ChannelId) as ISocketMessageChannel;
                if (channel == null) return;

                var message = await channel.GetMessageAsync(messageId) as IUserMessage;
                if (message == null) return;

                // Get current reaction count
                var reaction = message.Reactions.FirstOrDefault(r => r.Key.Name == "🎉");
                int participantCount = 0;
                
                if (reaction.Key != null)
                {
                    // Get all users who reacted and exclude bots
                    var users = await message.GetReactionUsersAsync(reaction.Key, reaction.Value.ReactionCount).FlattenAsync();
                    participantCount = users.Count(u => !u.IsBot);
                }

                var host = _client.GetUser(giveaway.HostId);
                var embed = CreateGiveawayEmbed(giveaway.Prize, giveaway.EndTime, giveaway.WinnerCount, host, participantCount);

                await message.ModifyAsync(m => m.Embed = embed);
                Console.WriteLine($"Giveaway {messageId}: Teilnehmerzahl aktualisiert auf {participantCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating giveaway display: {ex.Message}");
            }
        }

        private Embed CreateGiveawayEmbed(string prize, DateTime endTime, int winners, IUser host, int participants)
        {
            var timeLeft = endTime - DateTime.UtcNow;
            string timeText;

            if (timeLeft.TotalSeconds <= 0)
            {
                timeText = "**Beendet**";
            }
            else
            {
                var days = timeLeft.Days;
                var hours = timeLeft.Hours;
                var minutes = timeLeft.Minutes;

                if (days > 0)
                    timeText = $"**{days}d {hours}h {minutes}m**";
                else if (hours > 0)
                    timeText = $"**{hours}h {minutes}m**";
                else
                    timeText = $"**{minutes}m**";
            }

            return new EmbedBuilder()
                .WithTitle($"🎉 {prize} 🎉")
                .WithDescription($"Reagiere mit '🎉' um teilzunehmen!\n\n" +
                               $"**Endet:** {timeText}\n" +
                               $"**Gewinner:** {winners}\n" +
                               $"**Teilnehmer:** {participants}\n" +
                               $"**Veranstalter:** {host?.Mention ?? "Unbekannt"}")
                .WithColor(0xFFFFFF)
                .WithFooter($"Endet um {endTime:dd.MM.yyyy HH:mm} UTC")
                .WithTimestamp(endTime)
                .Build();
        }

        public List<GiveawayData> GetActiveGiveaways()
        {
            return _activeGiveaways.Values.Where(g => g.EndTime > DateTime.UtcNow).ToList();
        }

        private void LoadGiveaways()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var giveaways = JsonSerializer.Deserialize<List<GiveawayData>>(json);
                    
                    foreach (var giveaway in giveaways ?? new List<GiveawayData>())
                    {
                        if (giveaway.EndTime > DateTime.UtcNow)
                        {
                            _activeGiveaways[giveaway.MessageId] = giveaway;
                        }
                    }
                    
                    Console.WriteLine($"Loaded {_activeGiveaways.Count} active giveaways");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading giveaways: {ex.Message}");
            }
        }

        private void SaveGiveaways()
        {
            try
            {
                var giveaways = _activeGiveaways.Values.ToList();
                var json = JsonSerializer.Serialize(giveaways, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dataFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving giveaways: {ex.Message}");
            }
        }
    }

    public class GiveawayData
    {
        public ulong MessageId { get; set; }
        public ulong ChannelId { get; set; }
        public string Prize { get; set; } = "";
        public DateTime EndTime { get; set; }
        public int WinnerCount { get; set; }
        public ulong HostId { get; set; }
        public List<ulong> Participants { get; set; } = new();
    }
}