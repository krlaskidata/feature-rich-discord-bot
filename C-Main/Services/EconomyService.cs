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
    public class UserEconomyData
    {
        public ulong UserId { get; set; }
        public string Username { get; set; } = "";
        public long Balance { get; set; } = 1000; // Starting balance
        public long Bank { get; set; } = 0;
        public long TotalEarned { get; set; } = 1000;
        public long TotalSpent { get; set; } = 0;
        public int Level { get; set; } = 1;
        public long Experience { get; set; } = 0;
        public DateTime LastDaily { get; set; } = DateTime.MinValue;
        public DateTime LastWork { get; set; } = DateTime.MinValue;
        public DateTime LastCrime { get; set; } = DateTime.MinValue;
        public int WorkStreak { get; set; } = 0;
        public int DailyStreak { get; set; } = 0;
        
        // MEMORY OPTIMIZED: Only initialize collections when actually needed
        private Dictionary<string, int>? _achievements;
        private Dictionary<string, DateTime>? _cooldowns;
        private List<string>? _items; 
        private Dictionary<string, int>? _stats;
        
        public Dictionary<string, int> Achievements 
        { 
            get => _achievements ??= new Dictionary<string, int>(); 
            set => _achievements = value; 
        }
        
        public Dictionary<string, DateTime> Cooldowns 
        { 
            get => _cooldowns ??= new Dictionary<string, DateTime>(); 
            set => _cooldowns = value; 
        }
        
        public List<string> Items 
        { 
            get => _items ??= new List<string>(); 
            set => _items = value; 
        }
        
        public Dictionary<string, int> Stats 
        { 
            get => _stats ??= new Dictionary<string, int>(); 
            set => _stats = value; 
        }
    }

    public class GuildEconomyData
    {
        public ulong GuildId { get; set; }
        public Dictionary<ulong, UserEconomyData> Users { get; set; } = new();
        public long TaxRate { get; set; } = 5; // 5% tax on transactions
        public bool EconomyEnabled { get; set; } = true;
        public Dictionary<string, long> ItemPrices { get; set; } = new();
    }

    public static class EconomyService
    {
        private const string ECONOMY_FILE = "economy_data.json";
        private static Dictionary<ulong, GuildEconomyData> _economyData = LoadEconomyData();
        private static readonly Random _random = new();

        // Currency symbols and names
        public const string CURRENCY_SYMBOL = "🔸";
        public const string CURRENCY_NAME = "Credits";

        // Work jobs with different pay rates and requirements
        private static readonly Dictionary<string, (int minPay, int maxPay, int minLevel, string description)> WorkJobs = new()
        {
            ["programmer"] = (800, 1500, 1, "💻 You coded some features and squashed bugs"),
            ["designer"] = (600, 1200, 1, "🎨 You created some beautiful UI designs"),
            ["streamer"] = (400, 2000, 5, "🎮 You entertained viewers on your stream"),
            ["trader"] = (200, 3000, 10, "📈 You made some smart market trades"),
            ["hacker"] = (1000, 5000, 15, "⚡ You performed some... 'ethical' hacking"),
            ["ceo"] = (2000, 10000, 25, "👔 You managed your empire like a boss"),
            ["influencer"] = (500, 8000, 20, "📱 Your content went viral today"),
            ["scientist"] = (1200, 2500, 12, "🧪 You made breakthrough discoveries"),
            ["chef"] = (300, 800, 1, "👨‍🍳 You cooked up a storm in the kitchen"),
            ["musician"] = (400, 1800, 8, "🎵 Your music touched people's hearts")
        };

        private static Dictionary<ulong, GuildEconomyData> LoadEconomyData()
        {
            try
            {
                if (File.Exists(ECONOMY_FILE))
                {
                    var json = File.ReadAllText(ECONOMY_FILE);
                    return JsonSerializer.Deserialize<Dictionary<ulong, GuildEconomyData>>(json) ?? new Dictionary<ulong, GuildEconomyData>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading economy data: {ex.Message}");
            }
            return new Dictionary<ulong, GuildEconomyData>();
        }

        private static void SaveEconomyData()
        {
            try
            {
                // MEMORY OPTIMIZATION: Cleanup before saving
                CleanupInactiveUsers();
                
                var json = JsonSerializer.Serialize(_economyData, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(ECONOMY_FILE, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving economy data: {ex.Message}");
            }
        }
        
        private static void CleanupInactiveUsers()
        {
            try
            {
                const int MAX_USERS_PER_GUILD = 1000; // Reduced from 5000 to 1000 for memory optimization
                
                foreach (var guildData in _economyData.Values.ToList())
                {
                    // Remove users with 0 balance, no XP, and old last activity
                    var cutoffDate = DateTime.UtcNow.AddDays(-60); // 60 days inactive
                    
                    var inactiveUsers = guildData.Users
                        .Where(kvp => kvp.Value.Balance == 0 && 
                                     kvp.Value.Bank == 0 &&
                                     kvp.Value.Experience == 0 &&
                                     kvp.Value.LastDaily < cutoffDate &&
                                     kvp.Value.LastWork < cutoffDate)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var userId in inactiveUsers)
                        guildData.Users.Remove(userId);
                    
                    // Limit total users per guild
                    if (guildData.Users.Count > MAX_USERS_PER_GUILD)
                    {
                        var usersToRemove = guildData.Users
                            .OrderBy(kvp => kvp.Value.Balance + kvp.Value.Bank) // Remove poorest first
                            .Take(guildData.Users.Count - MAX_USERS_PER_GUILD)
                            .Select(kvp => kvp.Key)
                            .ToList();
                        
                        foreach (var userId in usersToRemove)
                            guildData.Users.Remove(userId);
                    }
                    
                    if (inactiveUsers.Count > 0)
                        Console.WriteLine($"Economy cleanup for guild {guildData.GuildId}: Removed {inactiveUsers.Count} inactive users. Total: {guildData.Users.Count}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during economy cleanup: {ex.Message}");
            }
        }

        public static UserEconomyData GetUserData(ulong guildId, ulong userId, string username)
        {
            if (!_economyData.ContainsKey(guildId))
            {
                _economyData[guildId] = new GuildEconomyData { GuildId = guildId };
            }

            var guildData = _economyData[guildId];
            if (!guildData.Users.ContainsKey(userId))
            {
                guildData.Users[userId] = new UserEconomyData 
                { 
                    UserId = userId, 
                    Username = username,
                    Stats = new Dictionary<string, int>
                    {
                        ["commands_used"] = 0,
                        ["games_played"] = 0,
                        ["work_sessions"] = 0,
                        ["daily_claims"] = 0,
                        ["gambles_won"] = 0,
                        ["gambles_lost"] = 0
                    }
                };
            }

            // Update username
            guildData.Users[userId].Username = username;
            return guildData.Users[userId];
        }

        public static bool AddMoney(ulong guildId, ulong userId, string username, long amount, string reason = "")
        {
            try
            {
                var userData = GetUserData(guildId, userId, username);
                userData.Balance += amount;
                userData.TotalEarned += amount;
                
                // Add experience (1 XP per 10 credits earned)
                AddExperience(guildId, userId, username, amount / 10);
                
                SaveEconomyData();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool RemoveMoney(ulong guildId, ulong userId, string username, long amount, string reason = "")
        {
            try
            {
                var userData = GetUserData(guildId, userId, username);
                if (userData.Balance < amount) return false;
                
                userData.Balance -= amount;
                userData.TotalSpent += amount;
                SaveEconomyData();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void AddExperience(ulong guildId, ulong userId, string username, long xp)
        {
            var userData = GetUserData(guildId, userId, username);
            userData.Experience += xp;
            
            // Level up calculation (exponential growth)
            var requiredXp = (long)(Math.Pow(userData.Level, 2) * 100);
            if (userData.Experience >= requiredXp)
            {
                userData.Level++;
                userData.Balance += userData.Level * 100; // Level up bonus
            }
            
            SaveEconomyData();
        }

        public static (bool success, string job, long amount, string description) Work(ulong guildId, ulong userId, string username)
        {
            var userData = GetUserData(guildId, userId, username);
            
            // Check cooldown (4 hours)
            if (DateTime.UtcNow < userData.LastWork.AddHours(4))
            {
                var timeLeft = userData.LastWork.AddHours(4) - DateTime.UtcNow;
                return (false, "", 0, $"You need to wait {timeLeft.Hours}h {timeLeft.Minutes}m before working again!");
            }

            // Get available jobs based on level
            var availableJobs = WorkJobs.Where(j => userData.Level >= j.Value.minLevel).ToList();
            if (!availableJobs.Any())
            {
                return (false, "", 0, "No jobs available for your level!");
            }

            // Select random job
            var selectedJob = availableJobs[_random.Next(availableJobs.Count)];
            var jobName = selectedJob.Key;
            var jobData = selectedJob.Value;
            
            // Calculate pay with level bonus
            var basePay = _random.Next(jobData.minPay, jobData.maxPay + 1);
            var levelBonus = (userData.Level - 1) * 0.1; // 10% bonus per level
            var streakBonus = Math.Min(userData.WorkStreak * 0.05, 1.0); // 5% per streak, max 100%
            var finalPay = (long)(basePay * (1 + levelBonus + streakBonus));

            // Update user data
            userData.LastWork = DateTime.UtcNow;
            userData.WorkStreak++;
            userData.Stats["work_sessions"]++;
            AddMoney(guildId, userId, username, finalPay, $"Work: {jobName}");

            SaveEconomyData();
            return (true, jobName, finalPay, jobData.description);
        }

        public static (bool success, long amount, int streak) Daily(ulong guildId, ulong userId, string username)
        {
            var userData = GetUserData(guildId, userId, username);
            
            // Check if daily is available (24 hours)
            if (DateTime.UtcNow < userData.LastDaily.AddDays(1))
            {
                var timeLeft = userData.LastDaily.AddDays(1) - DateTime.UtcNow;
                return (false, 0, userData.DailyStreak);
            }

            // Reset streak if more than 48 hours
            if (DateTime.UtcNow > userData.LastDaily.AddDays(2))
            {
                userData.DailyStreak = 0;
            }

            userData.DailyStreak++;
            userData.LastDaily = DateTime.UtcNow;
            userData.Stats["daily_claims"]++;

            // Calculate daily reward with streak bonus
            var baseDaily = 500;
            var streakBonus = Math.Min(userData.DailyStreak * 50, 2000); // Max 2000 bonus
            var levelBonus = userData.Level * 25;
            var totalReward = baseDaily + streakBonus + levelBonus;

            AddMoney(guildId, userId, username, totalReward, "Daily reward");
            SaveEconomyData();
            
            return (true, totalReward, userData.DailyStreak);
        }

        public static List<UserEconomyData> GetLeaderboard(ulong guildId, int count = 10)
        {
            if (!_economyData.ContainsKey(guildId)) return new List<UserEconomyData>();
            
            return _economyData[guildId].Users.Values
                .OrderByDescending(u => u.Balance + u.Bank)
                .Take(count)
                .ToList();
        }

        public static List<UserEconomyData> GetLevelLeaderboard(ulong guildId, int count = 10)
        {
            if (!_economyData.ContainsKey(guildId)) return new List<UserEconomyData>();
            
            return _economyData[guildId].Users.Values
                .OrderByDescending(u => u.Level)
                .ThenByDescending(u => u.Experience)
                .Take(count)
                .ToList();
        }

        public static void IncrementStat(ulong guildId, ulong userId, string username, string statName)
        {
            var userData = GetUserData(guildId, userId, username);
            if (!userData.Stats.ContainsKey(statName))
                userData.Stats[statName] = 0;
            userData.Stats[statName]++;
            SaveEconomyData();
        }

        public static bool CanAfford(ulong guildId, ulong userId, string username, long amount)
        {
            var userData = GetUserData(guildId, userId, username);
            return userData.Balance >= amount;
        }

        public static string FormatMoney(long amount)
        {
            return $"{CURRENCY_SYMBOL} {amount:N0}";
        }
    }
}