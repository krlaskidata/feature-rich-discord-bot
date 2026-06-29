using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace MainbotCSharp.Services
{
    public class BumpReminderData
    {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public DateTime NextBumpTime { get; set; }
        public bool IsActive { get; set; }
        public string Language { get; set; } = "de";
    }

    public static class BumpReminderService
    {
        private const string BUMP_REMINDERS_FILE = "bump_reminders.json";
        private const ulong DISBOARD_BOT_ID = 302050872383242240;
        private static Dictionary<ulong, BumpReminderData> _bumpReminders = LoadBumpReminders();
        private static readonly Dictionary<ulong, System.Threading.Timer> _activeTimers = new Dictionary<ulong, System.Threading.Timer>();
        private static DiscordSocketClient? _client;

        // MEMORY OPTIMIZED: Essential bump keywords only
        private static readonly string[] BumpKeywords = new[]
        {
            "bump done", ":thumbsup:", "bumped", "bump erfolgreich",
            "erfolgreich gebumpt", "server wurde gebumpt",
            "du kannst den server in 2 stunden wieder bumpen"
        };

        private static Dictionary<ulong, BumpReminderData> LoadBumpReminders()
        {
            try
            {
                if (!File.Exists(BUMP_REMINDERS_FILE))
                    return new Dictionary<ulong, BumpReminderData>();

                var json = File.ReadAllText(BUMP_REMINDERS_FILE);
                return JsonSerializer.Deserialize<Dictionary<ulong, BumpReminderData>>(json) ?? new Dictionary<ulong, BumpReminderData>();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Bump reminders load error: " + ex.Message);
                return new Dictionary<ulong, BumpReminderData>();
            }
        }

        private static void SaveBumpReminders()
        {
            try
            {
                var json = JsonSerializer.Serialize(_bumpReminders, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(BUMP_REMINDERS_FILE, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Bump reminders save error: " + ex.Message);
            }
        }

        public static void Initialize(DiscordSocketClient client)
        {
            _client = client;
            RestoreBumpReminders();
            Console.WriteLine("BumpReminderService initialized");
        }

        public static void RestoreBumpReminders()
        {
            foreach (var kvp in _bumpReminders)
            {
                var data = kvp.Value;
                if (data.IsActive && data.NextBumpTime > DateTime.Now)
                {
                    ScheduleBumpReminder(data);
                }
                else if (data.IsActive && data.NextBumpTime <= DateTime.Now)
                {
                    // Reminder should have triggered already, send it now
                    _ = Task.Run(() => SendBumpReminderAsync(data));
                }
            }
            Console.WriteLine($"✅ Restored {_bumpReminders.Count} bump reminders");
        }

        public static bool SetBumpReminder(ulong guildId, ulong channelId)
        {
            try
            {
                var nextBumpTime = DateTime.Now.AddHours(2);
                var data = new BumpReminderData
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    NextBumpTime = nextBumpTime,
                    IsActive = true,
                    Language = "en"
                };

                // Cancel existing timer for this guild
                if (_activeTimers.TryGetValue(guildId, out var existingTimer))
                {
                    existingTimer?.Dispose();
                    _activeTimers.Remove(guildId);
                }

                _bumpReminders[guildId] = data;
                SaveBumpReminders();

                ScheduleBumpReminder(data);
                Console.WriteLine($"✅ Bump reminder set for guild {guildId}, channel {channelId} at {nextBumpTime}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting bump reminder: {ex.Message}");
                return false;
            }
        }

        public static bool RemoveBumpReminder(ulong guildId)
        {
            try
            {
                if (_activeTimers.TryGetValue(guildId, out var timer))
                {
                    timer?.Dispose();
                    _activeTimers.Remove(guildId);
                }

                if (_bumpReminders.Remove(guildId))
                {
                    SaveBumpReminders();
                    Console.WriteLine($"✅ Bump reminder removed for guild {guildId}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing bump reminder: {ex.Message}");
                return false;
            }
        }

        private static void ScheduleBumpReminder(BumpReminderData data)
        {
            try
            {
                var delay = data.NextBumpTime - DateTime.Now;
                if (delay.TotalMilliseconds <= 0)
                {
                    // Should fire immediately
                    _ = Task.Run(() => SendBumpReminderAsync(data));
                    return;
                }

                var timer = new System.Threading.Timer(async _ =>
                {
                    await SendBumpReminderAsync(data);
                    _activeTimers.Remove(data.GuildId);
                }, null, delay, System.Threading.Timeout.InfiniteTimeSpan);

                _activeTimers[data.GuildId] = timer;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scheduling bump reminder: {ex.Message}");
            }
        }

        private static async Task SendBumpReminderAsync(BumpReminderData data)
        {
            try
            {
                if (_client == null) return;

                var guild = _client.GetGuild(data.GuildId);
                if (guild == null) return;

                var channel = guild.GetTextChannel(data.ChannelId);
                if (channel == null) return;

                var reminderMessages = new Dictionary<string, string[]>
                {
                    ["de"] = new[]
                    {
                        "🔔 **Bump Reminder!** Zeit zum Bumpen! Verwende `/bump` um den Server zu promoten!",
                        "⏰ **Bump Zeit!** Der Server kann wieder gebumpt werden! `/bump`",
                        "🚀 **Bump Erinnerung!** Vergiss nicht zu bumpen: `/bump`",
                        "📢 **Disboard Bump!** Zeit für einen neuen Bump! `/bump`"
                    },
                    ["en"] = new[]
                    {
                        "🔔 **Bump Reminder!** Time to bump! Use `/bump` to promote the server!",
                        "⏰ **Bump Time!** The server can be bumped again! `/bump`",
                        "🚀 **Bump Reminder!** Don't forget to bump: `/bump`",
                        "📢 **Disboard Bump!** Time for a new bump! `/bump`"
                    }
                };

                var messages = reminderMessages.TryGetValue(data.Language, out var msgs) ? msgs : reminderMessages["en"];
                var randomMessage = messages[new Random().Next(messages.Length)];

                await channel.SendMessageAsync(randomMessage);

                // Mark as inactive after sending
                data.IsActive = false;
                _bumpReminders[data.GuildId] = data;
                SaveBumpReminders();

                Console.WriteLine($"✅ Bump reminder sent to {guild.Name} #{channel.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending bump reminder: {ex.Message}");
            }
        }

        public static bool HandleDisboardMessage(SocketMessage message)
        {
            try
            {
                // Check if message is from Disboard bot
                if (message.Author.Id != DISBOARD_BOT_ID) return false;
                if (message.Embeds.Count == 0) return false;

                var embed = message.Embeds.FirstOrDefault();
                if (embed?.Description == null) return false;

                var description = embed.Description.ToLowerInvariant();

                // Check if any bump keywords are present
                bool isBumpMessage = BumpKeywords.Any(keyword => description.Contains(keyword.ToLowerInvariant()));
                if (!isBumpMessage) return false;

                Console.WriteLine($"🔍 Bump detected in channel: {message.Channel.Name}");

                // Set bump reminder for this guild/channel
                var guild = (message.Channel as SocketGuildChannel)?.Guild;
                if (guild != null)
                {
                    SetBumpReminder(guild.Id, message.Channel.Id);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling Disboard message: {ex.Message}");
                return false;
            }
        }

        public static BumpReminderData? GetBumpReminderStatus(ulong guildId)
        {
            return _bumpReminders.TryGetValue(guildId, out var data) ? data : null;
        }

        public static void SetBumpReminderLanguage(ulong guildId, string language)
        {
            if (_bumpReminders.TryGetValue(guildId, out var data))
            {
                data.Language = language;
                _bumpReminders[guildId] = data;
                SaveBumpReminders();
            }
        }

        public static List<BumpReminderData> GetAllActiveBumpReminders()
        {
            return _bumpReminders.Values.Where(data => data.IsActive).ToList();
        }
    }
}