using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace MainbotCSharp.Modules
{
    public class BirthdayData
    {
        public ulong ChannelId { get; set; }
        public Dictionary<ulong, string> Users { get; set; } = new Dictionary<ulong, string>();
        public HashSet<string> AnnouncedToday { get; set; } = new HashSet<string>(); // Track announced birthdays today
        public DateTime LastCleanup { get; set; } = DateTime.UtcNow; // MEMORY OPTIMIZATION: Track lastCleanup
    }

    public static class BirthdayService
    {
        private const string BIRTHDAYS_FILE = "birthdays.json";
        private static Dictionary<ulong, BirthdayData> _guildBirthdays = LoadBirthdayData();
        private static System.Threading.Timer? _dailyTimer;
        private static DiscordSocketClient? _client;

        private static Dictionary<ulong, BirthdayData> LoadBirthdayData()
        {
            try
            {
                if (!File.Exists(BIRTHDAYS_FILE))
                {
                    Console.WriteLine($"Birthday file {BIRTHDAYS_FILE} does not exist. Creating empty data.");
                    return new Dictionary<ulong, BirthdayData>();
                }

                var json = File.ReadAllText(BIRTHDAYS_FILE);
                var data = JsonSerializer.Deserialize<Dictionary<ulong, BirthdayData>>(json) ?? new Dictionary<ulong, BirthdayData>();
                Console.WriteLine($"Loaded birthday data from {BIRTHDAYS_FILE}. Found {data.Count} guilds.");
                return data;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading birthday data: {ex.Message}");
                return new Dictionary<ulong, BirthdayData>();
            }
        }

        private static void SaveBirthdayData()
        {
            try
            {
                // MEMORY OPTIMIZATION: Cleanup before saving
                CleanupBirthdayData();
                
                var json = JsonSerializer.Serialize(_guildBirthdays, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(BIRTHDAYS_FILE, json);
                Console.WriteLine($"Birthday data saved successfully. File: {BIRTHDAYS_FILE}");
                Console.WriteLine($"Data contains {_guildBirthdays.Count} guilds");
                foreach (var kvp in _guildBirthdays)
                {
                    Console.WriteLine($"  Guild {kvp.Key}: {kvp.Value.Users.Count} users, channel: {kvp.Value.ChannelId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Birthday save error: " + ex);
            }
        }
        
        // MEMORY OPTIMIZATION: Cleanup birthday data
        private static void CleanupBirthdayData()
        {
            try
            {
                const int MAX_USERS_PER_GUILD = 1000; // Reduced from 5000 to 1000 for memory optimization
                var removedUsers = 0;
                
                foreach (var kvp in _guildBirthdays.ToList())
                {
                    var guildId = kvp.Key;
                    var birthdayData = kvp.Value;
                    
                    // Clear AnnouncedToday if it's a new day (daily cleanup)
                    if ((DateTime.UtcNow - birthdayData.LastCleanup).TotalDays >= 1)
                    {
                        birthdayData.AnnouncedToday.Clear();
                        birthdayData.LastCleanup = DateTime.UtcNow;
                    }
                    
                    // Limit users per guild
                    if (birthdayData.Users.Count > MAX_USERS_PER_GUILD)
                    {
                        // Keep users randomly (or you could implement LRU logic here)
                        var usersToKeep = birthdayData.Users
                            .Take(MAX_USERS_PER_GUILD)
                            .ToDictionary(x => x.Key, x => x.Value);
                        
                        removedUsers += birthdayData.Users.Count - MAX_USERS_PER_GUILD;
                        birthdayData.Users = usersToKeep;
                    }
                }
                
                if (removedUsers > 0)
                    Console.WriteLine($"Birthday cleanup: Removed {removedUsers} excess birthday entries");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during birthday cleanup: {ex.Message}");
            }
        }

        public static void Initialize(DiscordSocketClient client)
        {
            _client = client;

            // Start daily birthday check timer (every hour)
            _dailyTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    await CheckBirthdaysAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Birthday check error: " + ex);
                }
            }, null, TimeSpan.Zero, TimeSpan.FromHours(1));

            // Migrate user names for all existing users
            _ = Task.Run(async () => await MigrateUserNames());
            
            Console.WriteLine("BirthdayService initialized");
        }

        private static async Task MigrateUserNames()
        {
            try
            {
                Console.WriteLine("🔄 Starting birthday user name migration...");
                bool hasChanges = false;
                
                foreach (var guildKvp in _guildBirthdays)
                {
                    var guildId = guildKvp.Key;
                    var data = guildKvp.Value;
                    var guild = _client?.GetGuild(guildId);
                    
                    if (guild == null) continue;
                    
                    foreach (var userKvp in data.Users.ToList())
                    {
                        var userId = userKvp.Key;
                        var birthday = userKvp.Value;
                        
                        var user = guild.GetUser(userId);
                        if (user != null)
                        {
                            Console.WriteLine($"✅ Resolved user: {user.Username} ({userId})");
                            hasChanges = true;
                        }
                        else
                        {
                            // Try to download user info if not in cache
                            try
                            {
                                if (_client != null)
                                {
                                    var restUser = await _client.GetUserAsync(userId);
                                    if (restUser != null)
                                    {
                                        Console.WriteLine($"✅ Downloaded user: {restUser.Username} ({userId})");
                                        hasChanges = true;
                                    }
                                }
                            }
                            catch
                            {
                                Console.WriteLine($"⚠️ Could not resolve user with ID: {userId}");
                            }
                        }
                    }
                }
                
                if (hasChanges)
                {
                    Console.WriteLine("✅ User name migration completed successfully!");
                }
                else
                {
                    Console.WriteLine("ℹ️ No user name migration needed.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during user name migration: {ex.Message}");
            }
        }

        public static void SetBirthdayChannel(ulong guildId, ulong channelId)
        {
            if (!_guildBirthdays.TryGetValue(guildId, out var data))
            {
                data = new BirthdayData();
                _guildBirthdays[guildId] = data;
            }

            data.ChannelId = channelId;
            SaveBirthdayData();
        }

        public static bool SetUserBirthday(ulong guildId, ulong userId, string birthday)
        {
            // Validate date format dd/mm/yyyy
            if (!Regex.IsMatch(birthday, @"^\d{2}\/\d{2}\/\d{4}$"))
            {
                Console.WriteLine($"Birthday validation failed: Invalid format '{birthday}' for user {userId}");
                return false;
            }

            // Try to parse to validate it's a real date
            try
            {
                var parts = birthday.Split('/');
                var day = int.Parse(parts[0]);
                var month = int.Parse(parts[1]);
                var year = int.Parse(parts[2]);

                // Basic validation
                if (day < 1 || day > 31 || month < 1 || month > 12 || year < 1900 || year > DateTime.Now.Year)
                {
                    Console.WriteLine($"Birthday validation failed: Invalid date values {day}/{month}/{year} for user {userId}");
                    return false;
                }

                // Try to create DateTime to validate
                var testDate = new DateTime(year, month, day);
                Console.WriteLine($"Birthday validation success: {testDate} for user {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Birthday validation failed: Exception {ex.Message} for user {userId}");
                return false;
            }

            if (!_guildBirthdays.TryGetValue(guildId, out var data))
            {
                data = new BirthdayData();
                _guildBirthdays[guildId] = data;
                Console.WriteLine($"Created new birthday data for guild {guildId}");
            }

            data.Users[userId] = birthday;
            SaveBirthdayData();
            Console.WriteLine($"Successfully saved birthday for user {userId} in guild {guildId}: {birthday}");
            return true;
        }

        public static bool RemoveUserBirthday(ulong guildId, ulong userId)
        {
            if (!_guildBirthdays.TryGetValue(guildId, out var data))
                return false;

            var removed = data.Users.Remove(userId);
            if (removed)
                SaveBirthdayData();

            return removed;
        }

        public static BirthdayData? GetGuildBirthdayData(ulong guildId)
        {
            var result = _guildBirthdays.TryGetValue(guildId, out var data) ? data : null;
            Console.WriteLine($"GetGuildBirthdayData for {guildId}: {(result != null ? $"Found data with {result.Users.Count} users" : "No data found")}");
            return result;
        }

        private static async Task CheckBirthdaysAsync()
        {
            if (_client == null) return;

            var today = DateTime.Now;
            var todayString = today.ToString("dd/MM");
            var todayKey = today.ToString("yyyy-MM-dd");

            foreach (var kvp in _guildBirthdays)
            {
                var guildId = kvp.Key;
                var data = kvp.Value;

                if (data.ChannelId == 0) continue; // No channel set

                try
                {
                    var guild = _client.GetGuild(guildId);
                    if (guild == null) continue;

                    var channel = guild.GetTextChannel(data.ChannelId);
                    if (channel == null) continue;

                    // Check each user's birthday
                    foreach (var userKvp in data.Users)
                    {
                        var userId = userKvp.Key;
                        var birthday = userKvp.Value;

                        // Extract day/month from stored birthday (dd/mm/yyyy)
                        var birthdayDayMonth = birthday.Substring(0, 5); // Gets dd/MM

                        if (birthdayDayMonth == todayString)
                        {
                            var announceKey = $"{todayKey}_{userId}";
                            
                            // Skip if already announced today
                            if (data.AnnouncedToday.Contains(announceKey))
                                continue;

                            try
                            {
                                var embed = new EmbedBuilder()
                                    .WithTitle("🎉 Happy Birthday! 🎂")
                                    .WithDescription($"★ Happy fucking birthday, you gorgeous thing! <@{userId}> 🎂🎉🎈")
                                    .WithColor(0x800080) // Purple
                                    .WithCurrentTimestamp()
                                    .Build();

                                await channel.SendMessageAsync(embed: embed);
                                
                                // Mark as announced
                                data.AnnouncedToday.Add(announceKey);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to send birthday message for user {userId}: {ex.Message}");
                            }
                        }
                    }
                    
                    // Clean up old announcement tracking (keep only today's)
                    data.AnnouncedToday.RemoveWhere(key => !key.StartsWith(todayKey));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Birthday check error for guild {guildId}: {ex.Message}");
                }
            }
        }
    }
}