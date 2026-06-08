using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace MainbotCSharp.Modules
{
    // =========================
    // Config Models
    // =========================

    public class SecurityConfigEntry
    {
        // Master switch
        public bool Enabled { get; set; } = false;

        // Where security events get logged (optional)
        public ulong? LogChannelId { get; set; } = null;

        // Behavior toggles (Sapphire-like calm defaults)
        public bool DeleteOnHighSeverity { get; set; } = true;
        public bool WarnUserOnMediumPlus { get; set; } = true;
        public bool TimeoutEnabled { get; set; } = false;

        // Timeout only after strikes threshold for High/Critical
        public int TimeoutSeconds { get; set; } = 600; // 10 min
        public int MaxStrikesBeforeTimeout { get; set; } = 3;

        // Prevent bot spamming warnings/logs for the same user repeatedly
        public int CooldownSeconds { get; set; } = 30;

        // Scam rules (false-positive safe)
        public bool BlockInvites { get; set; } = true;
        public bool BlockLinks { get; set; } = false; // default off; scam uses link+keyword gating anyway

        // Link whitelist (channels where links are allowed even if BlockLinks is on)
        public List<ulong> AllowedLinkChannelIds { get; set; } = new();

        // Ignore admins by default (also hard-coded in handler)
        public List<ulong> IgnoreRoleIds { get; set; } = new();
        public List<ulong> IgnoreChannelIds { get; set; } = new();
    }

    public class CleanupIntervalEntry
    {
        public ulong ChannelId { get; set; }
        public bool Enabled { get; set; } = true;
        public int IntervalHours { get; set; } = 1;
    }

    // =========================
    // Moderation Models
    // =========================

    internal enum ViolationCategory
    {
        None,
        Invite,
        GenericLink,
        Spam,
        Scam,
        HardLanguage,
        SoftLanguage,
        SerbianLanguage,
        NsfwAttachment
    }

    internal enum ViolationSeverity
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }

    internal sealed class ViolationResult
    {
        public bool IsViolation => Severity != ViolationSeverity.None && Category != ViolationCategory.None;
        public ViolationCategory Category { get; set; } = ViolationCategory.None;
        public ViolationSeverity Severity { get; set; } = ViolationSeverity.None;
        public string Reason { get; set; } = "—";
        public string Matched { get; set; } = "—";

        // Decision flags (computed)
        public bool ShouldDelete { get; set; }
        public bool ShouldWarn { get; set; }
        public bool ShouldStrike { get; set; }
        public bool ShouldTimeout { get; set; }
    }

    internal sealed class StrikeEntry
    {
        public int Strikes { get; set; }
        public DateTimeOffset LastActionAt { get; set; } = DateTimeOffset.MinValue;
    }

    // =========================
    // Embeds
    // =========================

    internal static class EmbedFactory
    {
        private static readonly Color Info = new Color(0x2F3136);
        private static readonly Color Ok = new Color(0x57F287);
        private static readonly Color Warn = new Color(0xFEE75C);
        private static readonly Color Bad = new Color(0xED4245);

        public static Embed BuildInfo(string title, string description)
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(Info)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

        public static Embed BuildSuccess(string title, string description)
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(Ok)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

        public static Embed BuildWarning(string title, string description)
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(Warn)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

        public static Embed BuildError(string title, string description)
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(Bad)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

        public static Embed BuildSecurityLog(SocketGuild guild, SocketUserMessage message, ViolationResult v, string actionTaken)
        {
            var user = message.Author;
            var chan = message.Channel;

            var desc = $"**Category:** {v.Category}\n" +
                       $"**Severity:** {v.Severity}\n" +
                       $"**Reason:** {v.Reason}\n" +
                       $"**Matched:** `{SafeSnippet(v.Matched, 120)}`\n" +
                       $"**Action:** {actionTaken}";

            var eb = new EmbedBuilder()
                .WithTitle("Security Event")
                .WithDescription(desc)
                .WithColor(v.Severity >= ViolationSeverity.High ? new Color(0xED4245) : new Color(0xFEE75C))
                .AddField("User", $"{user.Username}#{(user as SocketGuildUser)?.Discriminator ?? "0000"}\n`{user.Id}`", true)
                .AddField("Channel", $"{chan.Name}\n`{chan.Id}`", true)
                .AddField("Message", SafeSnippet(message.Content ?? "—", 300), false)
                .WithFooter($"Guild: {guild.Name} • {guild.Id}")
                .WithTimestamp(DateTimeOffset.UtcNow);

            return eb.Build();
        }

        public static Embed BuildSuspiciousJoinLog(SocketGuildUser user, IEnumerable<string> reasons)
        {
            var guild = user.Guild;
            var desc =
                $"**User:** {user.Mention}\n" +
                $"**User ID:** `{user.Id}`\n" +
                $"**Account Created:** <t:{user.CreatedAt.ToUnixTimeSeconds()}:F> (<t:{user.CreatedAt.ToUnixTimeSeconds()}:R>)\n" +
                $"**Joined:** <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F> (<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>)\n\n" +
                $"**Signals:**\n- {string.Join("\n- ", reasons)}\n\n" +
                $"**Suggested commands:**\n" +
                $"`!ban {user.Id} <reason>`\n" +
                $"`!kick {user.Id} <reason>`\n" +
                $"`!timeout {user.Id} <minutes> <reason>`";

            return new EmbedBuilder()
                .WithTitle("Suspicious User Joined")
                .WithDescription(desc)
                .WithColor(new Color(0xFEE75C))
                .WithThumbnailUrl(user.GetAvatarUrl(size: 256) ?? user.GetDefaultAvatarUrl())
                .WithFooter($"Guild: {guild.Name} • {guild.Id}")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
        }

        private static string SafeSnippet(string input, int max)
        {
            if (string.IsNullOrWhiteSpace(input)) return "—";
            input = input.Replace("`", "'");
            return input.Length <= max ? input : input.Substring(0, max) + "...";
        }
    }

    // =========================
    // Security Service
    // =========================

    public static class SecurityService
    {
        private const string SECURITY_FILE = "security_config.json";
        private const string CLEANUP_INTERVALS_FILE = "cleanup_intervals.json";

        private static Dictionary<ulong, SecurityConfigEntry> _config = LoadSecurityConfig();
        private static Dictionary<ulong, CleanupIntervalEntry> _cleanupIntervals = LoadCleanupIntervals();
        private static readonly Dictionary<ulong, System.Timers.Timer> _timers = new Dictionary<ulong, System.Timers.Timer>();

        // Strike tracking: (GuildId, UserId) -> entry - MEMORY OPTIMIZED!
        private static readonly Dictionary<(ulong GuildId, ulong UserId), StrikeEntry> _strikes = new();
        private static readonly object _strikeLock = new();
        
        // Memory optimization constants
        private const int MAX_STRIKES_ENTRIES = 1000; // Reduced from 5000 to 1000 for memory optimization
        private const int MAX_LOG_FILE_SIZE_MB = 10; // Rotate logs at 10MB
        private const int CLEANUP_INTERVAL_HOURS = 24; // Daily cleanup
        
        private static DateTime _lastCleanup = DateTime.UtcNow;

        // -------------------------
        // Regex (compiled, safe)
        // -------------------------

        private static readonly Regex InviteRegex =
            new(@"(discord\.gg\/|discord(app)?\.com\/invite\/)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LinkRegex =
            new(@"(https?:\/\/|www\.)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Scam keywords: ONLY used when message contains a link
        private static readonly List<Regex> ScamKeywords = new()
        {
            new(@"free\s*nitro", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"discord\s*gift", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"steam\s*gift", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"crypto\s*airdrop", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"claim\s*(now|here)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"limited\s*time", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\b(nudes?|nude|porn|nsfw|adult)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // Hard block: keep only truly severe items (minimal, less false positives)
        private static readonly List<Regex> HardBlockPatterns = new()
        {
            new(@"\b(kys|kill\s+yourself)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bsuicid(e|al)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\b(porn(hub)?|nudes?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bretard(ed)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // Soft block: warn/log only (no timeouts)
        private static readonly List<Regex> SoftBlockPatterns = new()
        {
            new(@"\bidiot(ic)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bdumb(ass)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bbastard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bslut\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bhure\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // Serbian patterns: keep your existing list
        private static readonly List<Regex> SerbianPatterns = new()
        {
            new(@"\bkur(a|ac|cem|cu)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bpick(a|u|e)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bjebi(ga|te|em)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bjebem\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bgovno\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        private static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            return Regex.Replace(
                input.ToLower()
                     .Replace("@", "a")
                     .Replace("4", "a")
                     .Replace("1", "i")
                     .Replace("!", "i")
                     .Replace("0", "o")
                     .Replace("5", "s")
                     .Replace("$", "s")
                     .Replace("3", "e"),
                @"[^a-z\s]",
                ""
            );
        }

        // -------------------------
        // Config load/save
        // -------------------------

        private static Dictionary<ulong, SecurityConfigEntry> LoadSecurityConfig()
        {
            try
            {
                if (!File.Exists(SECURITY_FILE)) return new Dictionary<ulong, SecurityConfigEntry>();
                var txt = File.ReadAllText(SECURITY_FILE);
                var d = JsonSerializer.Deserialize<Dictionary<ulong, SecurityConfigEntry>>(txt);
                return d ?? new Dictionary<ulong, SecurityConfigEntry>();
            }
            catch
            {
                return new Dictionary<ulong, SecurityConfigEntry>();
            }
        }

        private static Dictionary<ulong, CleanupIntervalEntry> LoadCleanupIntervals()
        {
            try
            {
                if (!File.Exists(CLEANUP_INTERVALS_FILE)) return new Dictionary<ulong, CleanupIntervalEntry>();
                var txt = File.ReadAllText(CLEANUP_INTERVALS_FILE);
                var d = JsonSerializer.Deserialize<Dictionary<ulong, CleanupIntervalEntry>>(txt);
                return d ?? new Dictionary<ulong, CleanupIntervalEntry>();
            }
            catch
            {
                return new Dictionary<ulong, CleanupIntervalEntry>();
            }
        }

        private static void SaveSecurityConfig()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SECURITY_FILE, txt);
            }
            catch { }
        }

        private static void SaveCleanupIntervals()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_cleanupIntervals, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(CLEANUP_INTERVALS_FILE, txt);
            }
            catch { }
        }

        public static void SetConfig(ulong guildId, SecurityConfigEntry entry)
        {
            _config[guildId] = entry;
            SaveSecurityConfig();
        }

        public static SecurityConfigEntry GetConfig(ulong guildId)
        {
            return _config.TryGetValue(guildId, out var e) ? e : new SecurityConfigEntry();
        }

        // -------------------------
        // Cleanup interval feature
        // -------------------------

        public static void SetCleanupInterval(ulong guildId, ulong channelId, DiscordSocketClient client, int intervalHours = 1)
        {
            _cleanupIntervals[guildId] = new CleanupIntervalEntry { ChannelId = channelId, IntervalHours = intervalHours };
            SaveCleanupIntervals();

            if (_timers.TryGetValue(guildId, out var existingTimer))
            {
                existingTimer.Stop();
                existingTimer.Dispose();
                _timers.Remove(guildId);
            }

            var timer = new System.Timers.Timer(TimeSpan.FromHours(intervalHours).TotalMilliseconds);
            timer.Elapsed += async (_, __) =>
            {
                try
                {
                    var guild = client.GetGuild(guildId);
                    var channel = guild?.GetTextChannel(channelId);
                    if (channel != null)
                        await PerformScheduledCleanup(channel);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cleanup timer error: {ex.Message}");
                }
            };
            timer.Start();
            _timers[guildId] = timer;
        }

        public static void RemoveCleanupInterval(ulong guildId)
        {
            _cleanupIntervals.Remove(guildId);
            SaveCleanupIntervals();

            if (_timers.TryGetValue(guildId, out var timer))
            {
                timer.Stop();
                timer.Dispose();
                _timers.Remove(guildId);
            }
        }

        public static CleanupIntervalEntry? GetCleanupInterval(ulong guildId)
        {
            return _cleanupIntervals.TryGetValue(guildId, out var entry) ? entry : null;
        }

        private static async Task PerformScheduledCleanup(SocketTextChannel channel)
        {
            try
            {
                bool hasMore = true;
                int totalDeleted = 0;

                while (hasMore)
                {
                    var messages = await channel.GetMessagesAsync(100).FlattenAsync();
                    var deleteable = messages.Where(x =>
                        DateTimeOffset.UtcNow - x.Timestamp < TimeSpan.FromDays(14) &&
                        !x.IsPinned).ToList();

                    if (!deleteable.Any())
                    {
                        hasMore = false;
                        break;
                    }

                    if (deleteable.Count == 1)
                    {
                        await deleteable.First().DeleteAsync();
                        totalDeleted += 1;
                        hasMore = false;
                    }
                    else
                    {
                        await channel.DeleteMessagesAsync(deleteable);
                        totalDeleted += deleteable.Count;
                    }

                    await Task.Delay(1000);
                }

                if (totalDeleted > 0)
                {
                    var embed = EmbedFactory.BuildInfo(
                        "Scheduled Cleanup Completed",
                        $"Deleted **{totalDeleted}** messages in {channel.Mention} (excluding pinned messages).");

                    var notification = await channel.SendMessageAsync(embed: embed);

                    _ = Task.Delay(45000).ContinueWith(async _ =>
                    {
                        try { await notification.DeleteAsync(); } catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Scheduled cleanup error: {ex.Message}");
            }
        }

        // -------------------------
        // Join suspicious user feature
        // -------------------------

        public static async Task HandleUserJoinedAsync(SocketGuildUser user)
        {
            try
            {
                var cfg = GetConfig(user.Guild.Id);
                if (!cfg.Enabled) return;

                var reasons = new List<string>();

                var accountAge = DateTimeOffset.UtcNow - user.CreatedAt;
                if (accountAge.TotalDays < 20)
                    reasons.Add($"Account age < 20 days ({accountAge.TotalDays:F0} days)");

                var username = user.Username.ToLowerInvariant();
                var suspiciousUsernamePatterns = new[]
                {
                    @"discord\.gg", @"bit\.ly", @"tinyurl", @"shorturl",
                    @"admin\d+", @"moderator\d+", @"staff\d+",
                    @"[0-9]{8,}",
                    @"^[a-z]{1,3}[0-9]{4,}$",
                    @"free.*nitro", @"nitro.*free", @"giveaway",
                    @"crypto", @"bitcoin", @"trade", @"invest",
                    @"porn", @"sex", @"nude"
                };

                foreach (var pattern in suspiciousUsernamePatterns)
                {
                    if (Regex.IsMatch(username, pattern, RegexOptions.IgnoreCase))
                    {
                        reasons.Add($"Suspicious username pattern: {pattern}");
                        break;
                    }
                }

                var avatarUrl = user.GetAvatarUrl(size: 256);
                if (avatarUrl == null)
                    reasons.Add("No custom profile picture");

                if (user.Discriminator != null)
                {
                    var disc = user.Discriminator;
                    if (disc.Length > 0 && disc.All(c => c == disc[0]))
                        reasons.Add($"Suspicious discriminator pattern: #{disc}");
                }

                if (reasons.Count == 0) return;

                await LogSuspiciousUser(user, reasons);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HandleUserJoined error: {ex.Message}");
            }
        }

        private static async Task LogSuspiciousUser(SocketGuildUser user, List<string> reasons)
        {
            try
            {
                var cfg = GetConfig(user.Guild.Id);
                if (!cfg.LogChannelId.HasValue) return;

                var logChannel = user.Guild.GetTextChannel(cfg.LogChannelId.Value);
                if (logChannel == null) return;

                var embed = EmbedFactory.BuildSuspiciousJoinLog(user, reasons);
                await logChannel.SendMessageAsync(embed: embed);

                try
                {
                    var logEntry = new
                    {
                        timestamp = DateTimeOffset.UtcNow,
                        type = "suspicious_join",
                        guildId = user.Guild.Id,
                        guildName = user.Guild.Name,
                        userId = user.Id,
                        username = $"{user.Username}#{user.Discriminator}",
                        accountAgeDays = (DateTimeOffset.UtcNow - user.CreatedAt).TotalDays,
                        reasons
                    };
                    WriteToRotatingLogFile("suspicious_users.jsonl", JsonSerializer.Serialize(logEntry));
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LogSuspiciousUser error: {ex.Message}");
            }
        }

        // -------------------------
        // Message moderation
        // -------------------------

        public static async Task HandleMessageAsync(SocketMessage rawMessage)
        {
            try
            {
                if (rawMessage is not SocketUserMessage message) return;
                if (message.Author.IsBot) return;
                if (message.Channel is not SocketTextChannel tchan) return;

                var guild = tchan.Guild;
                if (guild == null) return;

                var cfg = GetConfig(guild.Id);
                
                // DEBUG: Log message for troubleshooting
                Console.WriteLine($"[SECURITY] Guild={guild.Name} ({guild.Id}), User={message.Author.Username}, Enabled={cfg.Enabled}, Content='{message.Content}'");
                
                if (!cfg.Enabled) return;

                // Ignore configured channels
                if (cfg.IgnoreChannelIds.Contains(tchan.Id)) return;

                // Ignore admins (as you already did)
                if (message.Author is SocketGuildUser gUser)
                {
                    if (gUser.GuildPermissions.Administrator) return;
                    if (cfg.IgnoreRoleIds != null && cfg.IgnoreRoleIds.Count > 0)
                    {
                        if (gUser.Roles.Any(r => cfg.IgnoreRoleIds.Contains(r.Id)))
                            return;
                    }
                }

                var rawContent = message.Content ?? string.Empty;
                var normalized = Normalize(rawContent);

                bool containsInvite = InviteRegex.IsMatch(rawContent);
                bool containsLink = containsInvite || LinkRegex.IsMatch(rawContent);
                
                Console.WriteLine($"[SECURITY-CHECK] Invite={containsInvite}, Link={containsLink}, BlockInvites={cfg.BlockInvites}, BlockLinks={cfg.BlockLinks}");

                // 1) Invites
                if (cfg.BlockInvites && containsInvite)
                {
                    Console.WriteLine($"[SECURITY-VIOLATION] User {message.Author.Username} posted invite link");
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.Invite,
                        Severity = ViolationSeverity.High,
                        Reason = "Invite link detected",
                        Matched = "discord invite"
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 2) Spam: repeated chars/patterns
                if (Regex.IsMatch(rawContent, @"([a-zA-Z0-9])\1{6,}") || Regex.IsMatch(rawContent, @"(.)\s*\1{6,}"))
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.Spam,
                        Severity = ViolationSeverity.Medium,
                        Reason = "Spam pattern detected",
                        Matched = "repeated characters"
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 3) Scam: ONLY if link + keyword
                bool scamKeyword = containsLink && ScamKeywords.Any(r => r.IsMatch(normalized));
                Console.WriteLine($"[SECURITY-CHECK] ScamCheck: Link={containsLink}, HasScamKeyword={scamKeyword}");
                
                if (scamKeyword)
                {
                    Console.WriteLine($"[SECURITY-VIOLATION] User {message.Author.Username} posted potential scam (link + keyword)");
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.Scam,
                        Severity = ViolationSeverity.High,
                        Reason = "Potential scam message (keyword + link)",
                        Matched = "scam keyword + link"
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 4) Block generic links (optional, conservative)
                if (cfg.BlockLinks && containsLink && !cfg.AllowedLinkChannelIds.Contains(tchan.Id))
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.GenericLink,
                        Severity = ViolationSeverity.Medium,
                        Reason = "Link posting is restricted in this channel",
                        Matched = "link"
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 5) Hard language
                var hardMatch = HardBlockPatterns.FirstOrDefault(r => r.IsMatch(normalized));
                if (hardMatch != null)
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.HardLanguage,
                        Severity = ViolationSeverity.Critical,
                        Reason = "Severe language detected",
                        Matched = hardMatch.ToString()
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 6) Soft language
                var softMatch = SoftBlockPatterns.FirstOrDefault(r => r.IsMatch(normalized));
                if (softMatch != null)
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.SoftLanguage,
                        Severity = ViolationSeverity.Medium,
                        Reason = "Inappropriate language detected",
                        Matched = softMatch.ToString()
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 7) Serbian language patterns
                var serbMatch = SerbianPatterns.FirstOrDefault(r => r.IsMatch(normalized));
                if (serbMatch != null)
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.SerbianLanguage,
                        Severity = ViolationSeverity.Medium,
                        Reason = "Inappropriate language detected",
                        Matched = serbMatch.ToString()
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 8) Attachments (filename scan)
                if (message.Attachments != null && message.Attachments.Count > 0)
                {
                    foreach (var att in message.Attachments)
                    {
                        var name = (att.Filename ?? "").ToLowerInvariant();
                        if (Regex.IsMatch(name, "(nude|nudes|porn|dick|boobs|sex|pussy|tits|vagina|penis|clit|anal|nsfw|xxx|18\\+)",
                            RegexOptions.IgnoreCase))
                        {
                            var v = new ViolationResult
                            {
                                Category = ViolationCategory.NsfwAttachment,
                                Severity = ViolationSeverity.High,
                                Reason = "NSFW attachment detected",
                                Matched = name
                            };
                            await HandleViolationAsync(message, guild, cfg, v);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SecurityService error: " + ex);
            }
        }

        private static async Task HandleViolationAsync(SocketUserMessage message, SocketGuild guild, SecurityConfigEntry cfg, ViolationResult v)
        {
            // Decide action flags (calm, Sapphire-ish)
            // Default mapping:
            // - Medium: warn + log, do not delete unless configured by "DeleteOnHighSeverity" (only applies to High+)
            // - High: delete + warn + log
            // - Critical: delete + warn + strike + optional timeout after threshold
            v.ShouldWarn = cfg.WarnUserOnMediumPlus && v.Severity >= ViolationSeverity.Medium;
            v.ShouldDelete = cfg.DeleteOnHighSeverity && v.Severity >= ViolationSeverity.High;
            v.ShouldStrike = v.Severity >= ViolationSeverity.High;

            // MEMORY OPTIMIZATION: Cleanup old strikes periodically
            PerformMemoryCleanupIfNeeded();

            // Cooldown check: prevent spammy bot behavior
            var authorId = message.Author.Id;
            var key = (guild.Id, authorId);
            StrikeEntry entry;

            lock (_strikeLock)
            {
                if (!_strikes.TryGetValue(key, out entry!))
                {
                    // MEMORY CHECK: Don't add new entries if we're at capacity
                    if (_strikes.Count >= MAX_STRIKES_ENTRIES)
                    {
                        // Remove oldest entries (those with oldest LastActionAt)
                        var oldestEntries = _strikes
                            .OrderBy(kvp => kvp.Value.LastActionAt)
                            .Take(_strikes.Count / 4) // Remove 25% of oldest entries
                            .Select(kvp => kvp.Key)
                            .ToList();
                        
                        foreach (var oldKey in oldestEntries)
                            _strikes.Remove(oldKey);
                    }
                    
                    entry = new StrikeEntry();
                    _strikes[key] = entry;
                }

                var cd = TimeSpan.FromSeconds(Math.Max(5, cfg.CooldownSeconds));
                if (DateTimeOffset.UtcNow - entry.LastActionAt < cd)
                {
                    // Still log quietly (optional), but avoid DM / repeated actions
                    v.ShouldWarn = false;
                }

                entry.LastActionAt = DateTimeOffset.UtcNow;

                if (v.ShouldStrike)
                    entry.Strikes += 1;

                // Timeout only if enabled, severity high+, and strikes threshold reached
                if (cfg.TimeoutEnabled && v.Severity >= ViolationSeverity.High && entry.Strikes >= Math.Max(1, cfg.MaxStrikesBeforeTimeout))
                    v.ShouldTimeout = true;
            }

            string actionTaken = "Log";

            // Delete first (if configured) to prevent further spread
            if (v.ShouldDelete)
            {
                try { await message.DeleteAsync(); actionTaken = "Deleted"; }
                catch { actionTaken = "Attempted delete"; }
            }

            // Warn user via DM (embed-only)
            if (v.ShouldWarn)
            {
                try
                {
                    var dm = EmbedFactory.BuildWarning(
                        "Moderation Notice",
                        $"Your message was flagged in **{guild.Name}**.\n\n" +
                        $"**Reason:** {v.Reason}\n" +
                        $"**Category:** {v.Category}\n\n" +
                        $"Please follow the server rules.");

                    await message.Author.SendMessageAsync(embed: dm);
                    actionTaken = actionTaken == "Deleted" ? "Deleted + Warned" : "Warned";
                }
                catch
                {
                    // DM failed: do nothing (still log to mod channel)
                }
            }

            // Timeout (only if enabled and threshold reached)
            if (v.ShouldTimeout && message.Author is SocketGuildUser gUser)
            {
                try
                {
                    var duration = TimeSpan.FromSeconds(Math.Max(60, cfg.TimeoutSeconds));
                    await gUser.SetTimeOutAsync(duration, new RequestOptions { AuditLogReason = "Security system timeout threshold reached" });
                    actionTaken += " + Timeout";
                }
                catch
                {
                    actionTaken += " + Timeout failed";
                }
            }

            // Log channel (embed-only)
            if (cfg.LogChannelId.HasValue)
            {
                try
                {
                    var ch = guild.GetTextChannel(cfg.LogChannelId.Value);
                    if (ch != null)
                    {
                        var embed = EmbedFactory.BuildSecurityLog(guild, message, v, actionTaken);
                        await ch.SendMessageAsync(embed: embed);
                    }
                }
                catch { }
            }

            // Append jsonl log with rotation (MEMORY OPTIMIZED)
            try
            {
                var log = new
                {
                    time = DateTimeOffset.UtcNow,
                    guildId = guild.Id,
                    guildName = guild.Name,
                    userId = message.Author.Id,
                    userTag = message.Author.Username,
                    category = v.Category.ToString(),
                    severity = v.Severity.ToString(),
                    action = actionTaken,
                    matched = v.Matched,
                    content = message.Content
                };
                WriteToRotatingLogFile("security_logs_main.jsonl", JsonSerializer.Serialize(log));
            }
            catch { }
        }

        // =========================
        // MEMORY OPTIMIZATION METHODS
        // =========================
        
        private static void PerformMemoryCleanupIfNeeded()
        {
            // Only run cleanup every 24 hours
            if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromHours(CLEANUP_INTERVAL_HOURS))
                return;

            _lastCleanup = DateTime.UtcNow;
            
            lock (_strikeLock)
            {
                // Remove strikes older than 7 days
                var cutoffDate = DateTimeOffset.UtcNow.AddDays(-7);
                var expiredKeys = _strikes
                    .Where(kvp => kvp.Value.LastActionAt < cutoffDate)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in expiredKeys)
                    _strikes.Remove(key);
                
                Console.WriteLine($"Security cleanup: Removed {expiredKeys.Count} expired strike entries. Current count: {_strikes.Count}");
            }
        }
        
        private static void WriteToRotatingLogFile(string fileName, string jsonContent)
        {
            try
            {
                var fileInfo = new FileInfo(fileName);
                
                // Rotate log if it exceeds size limit
                if (fileInfo.Exists && fileInfo.Length > MAX_LOG_FILE_SIZE_MB * 1024 * 1024)
                {
                    var backupName = $"{fileName}.{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";
                    File.Move(fileName, backupName);
                    Console.WriteLine($"Rotated log file {fileName} to {backupName}");
                }
                
                File.AppendAllText(fileName, jsonContent + "\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to rotating log {fileName}: {ex.Message}");
            }
        }
    }

    // =========================
    // Commands (prefix !)
    // =========================

    public class SecurityCommands : ModuleBase<SocketCommandContext>
    {
        [Command("setsecuritymod")]
        [Summary("Setup security system (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task SecuritySetupAsync()
        {
            try
            {
                var ask = EmbedFactory.BuildInfo(
                    "Security Setup",
                    "Provide the **Channel ID** for the security log channel.\n" +
                    "Or type `new-securechan` to create a new log channel (requires a category ID).");

                await ReplyAsync(embed: ask);

                var channelResponse = await NextMessageAsync(TimeSpan.FromMinutes(1));
                if (channelResponse == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Timeout", "Setup timed out. Run `!setsecuritymod` again."));
                    return;
                }

                ulong logChannelId;
                ITextChannel logChannel;

                if (channelResponse.Content.Trim().Equals("new-securechan", StringComparison.OrdinalIgnoreCase))
                {
                    await ReplyAsync(embed: EmbedFactory.BuildInfo("Category Required", "Provide the **Category ID** where the log channel should be created."));

                    var categoryResponse = await NextMessageAsync(TimeSpan.FromMinutes(1));
                    if (categoryResponse == null)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Timeout", "Setup timed out. Run `!setsecuritymod` again."));
                        return;
                    }

                    if (!ulong.TryParse(categoryResponse.Content.Trim(), out var categoryId))
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Input", "Category ID was invalid."));
                        return;
                    }

                    var category = Context.Guild.GetCategoryChannel(categoryId);
                    if (category == null)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Not Found", "Category not found in this server."));
                        return;
                    }

                    logChannel = await Context.Guild.CreateTextChannelAsync("security-log", props => props.CategoryId = categoryId);
                    logChannelId = logChannel.Id;

                    await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                        "Channel Created",
                        $"Created security log channel: {logChannel.Mention}\nCategory: **{category.Name}**"));
                }
                else if (ulong.TryParse(channelResponse.Content.Trim(), out logChannelId))
                {
                    logChannel = Context.Guild.GetTextChannel(logChannelId);
                    if (logChannel == null)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Not Found", "Channel not found in this server."));
                        return;
                    }

                    await ReplyAsync(embed: EmbedFactory.BuildSuccess("Channel Set", $"Security log channel set to {logChannel.Mention}"));
                }
                else
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Input", "Provide a valid Channel ID or type `new-securechan`."));
                    return;
                }

                var config = new SecurityConfigEntry
                {
                    Enabled = true,
                    LogChannelId = logChannelId,
                    // Defaults already set in model; keep calm behavior
                };

                SecurityService.SetConfig(Context.Guild.Id, config);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Security Enabled",
                    $"Security monitoring is now active.\nLog Channel: <#{logChannelId}>\n\n" +
                    "Enabled checks:\n" +
                    "- Invite detection\n" +
                    "- Spam detection\n" +
                    "- Scam (link + keyword)\n" +
                    "- Language filters (severity-based)\n" +
                    "- Attachment filename scan\n" +
                    "- Suspicious join logging"));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Setup Failed", ex.Message));
            }
        }

        [Command("disable")]
        [Summary("Disable security system (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task SecurityDisableAsync()
        {
            try
            {
                var cfg = SecurityService.GetConfig(Context.Guild.Id);
                cfg.Enabled = false;
                SecurityService.SetConfig(Context.Guild.Id, cfg);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess("Security Disabled", "Security monitoring is now disabled for this server."));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Disable Failed", ex.Message));
            }
        }

        [Command("status")]
        [Summary("Check security system status (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SecurityStatusAsync()
        {
            try
            {
                var config = SecurityService.GetConfig(Context.Guild.Id);

                var desc =
                    $"**Enabled:** {(config.Enabled ? "Yes" : "No")}\n" +
                    $"**Log Channel:** {(config.LogChannelId.HasValue ? $"<#{config.LogChannelId.Value}>" : "Not set")}\n" +
                    $"**Timeout Enabled:** {(config.TimeoutEnabled ? "Yes" : "No")}\n" +
                    $"**Timeout Seconds:** {config.TimeoutSeconds}\n" +
                    $"**Strikes for Timeout:** {config.MaxStrikesBeforeTimeout}\n" +
                    $"**Cooldown Seconds:** {config.CooldownSeconds}\n" +
                    $"**Block Invites:** {(config.BlockInvites ? "Yes" : "No")}\n" +
                    $"**Block Links:** {(config.BlockLinks ? "Yes" : "No")}";

                await ReplyAsync(embed: EmbedFactory.BuildInfo("Security Status", desc));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Status Failed", ex.Message));
            }
        }

        // -------------------------
        // Cleanup commands (embed-only)
        // -------------------------

        [Command("cleanup-now")]
        [Summary("Clean all messages in current channel")]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [RequireBotPermission(GuildPermission.ManageMessages)]
        public async Task CleanupNowAsync()
        {
            try
            {
                var start = await ReplyAsync(embed: EmbedFactory.BuildInfo("Cleanup Started", $"Cleaning messages in {Context.Channel.Name}."));

                int totalDeleted = 0;
                bool hasMore = true;

                while (hasMore)
                {
                    var messages = await Context.Channel.GetMessagesAsync(100).FlattenAsync();
                    var deleteable = messages.Where(x => !x.IsPinned && x.Id != start.Id).ToList();

                    if (!deleteable.Any())
                    {
                        hasMore = false;
                        break;
                    }

                    var recent = deleteable.Where(x => DateTimeOffset.UtcNow - x.Timestamp < TimeSpan.FromDays(14)).ToList();
                    var old = deleteable.Where(x => DateTimeOffset.UtcNow - x.Timestamp >= TimeSpan.FromDays(14)).ToList();

                    if (recent.Count > 1)
                    {
                        try
                        {
                            await (Context.Channel as ITextChannel)!.DeleteMessagesAsync(recent);
                            totalDeleted += recent.Count;
                        }
                        catch
                        {
                            foreach (var msg in recent)
                            {
                                try { await msg.DeleteAsync(); totalDeleted++; } catch { }
                            }
                        }
                    }
                    else if (recent.Count == 1)
                    {
                        try { await recent.First().DeleteAsync(); totalDeleted++; } catch { }
                    }

                    foreach (var msg in old)
                    {
                        try { await msg.DeleteAsync(); totalDeleted++; await Task.Delay(100); } catch { }
                    }

                    if (messages.Count() < 100) hasMore = false;
                }

                try { await start.DeleteAsync(); } catch { }

                var result = await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Cleanup Complete",
                    $"Deleted **{totalDeleted}** messages in {Context.Channel.Name}."));

                _ = Task.Delay(5000).ContinueWith(async _ => { try { await result.DeleteAsync(); } catch { } });
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Cleanup Failed", ex.Message));
            }
        }

        [Command("cleanup-intervall")]
        [Alias("setcleanupinterval")]
        [Summary("Set automatic cleanup interval for a channel")]
        public async Task SetCleanupIntervalAsync(ulong channelId, string interval = "1h")
        {
            try
            {
                var channel = Context.Guild.GetTextChannel(channelId);
                if (channel == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Channel Not Found", "Provide a valid channel ID."));
                    return;
                }

                int hours = 1;
                string display = "1 hour";

                if (interval.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                {
                    var hoursStr = interval.Substring(0, interval.Length - 1);
                    if (!int.TryParse(hoursStr, out hours) || hours < 1 || hours > 720)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Interval", "Use format like `10h` (1-720 hours)."));
                        return;
                    }
                    display = $"{hours} hour{(hours > 1 ? "s" : "")}";
                }
                else if (interval.EndsWith("d", StringComparison.OrdinalIgnoreCase))
                {
                    var daysStr = interval.Substring(0, interval.Length - 1);
                    if (!int.TryParse(daysStr, out int days) || days < 1 || days > 30)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Interval", "Use format like `5d` (1-30 days)."));
                        return;
                    }
                    hours = days * 24;
                    display = $"{days} day{(days > 1 ? "s" : "")}";
                }
                else
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Format", "Use `10h` or `5d`. Example: `!cleanup-intervall CHANNEL_ID 5d`."));
                    return;
                }

                SecurityService.SetCleanupInterval(Context.Guild.Id, channelId, Context.Client as DiscordSocketClient, hours);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Cleanup Interval Set",
                    $"Channel: <#{channelId}>\nInterval: **{display}**\nStatus: Active"));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Failed", ex.Message));
            }
        }

        [Command("delcleanup-intervall")]
        [Alias("cleanupdel", "removecleanupinterval")]
        [Summary("Remove automatic cleanup interval for a channel")]
        public async Task RemoveCleanupIntervalAsync(ulong channelId)
        {
            try
            {
                var current = SecurityService.GetCleanupInterval(Context.Guild.Id);
                if (current == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Not Configured", "No cleanup interval is set for this server."));
                    return;
                }

                if (current.ChannelId != channelId)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Mismatch", $"Cleanup interval is set for <#{current.ChannelId}>, not <#{channelId}>."));
                    return;
                }

                SecurityService.RemoveCleanupInterval(Context.Guild.Id);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Cleanup Interval Removed",
                    $"Automatic cleanup disabled for <#{channelId}>."));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Failed", ex.Message));
            }
        }

        // -------------------------
        // Dangerous command (hardened)
        // -------------------------

        [Command("cleanup-server-ultra")]
        [Summary("Delete ALL channels and categories (Owner only, requires confirmation phrase).")]
        [RequireUserPermission(GuildPermission.Administrator)]
        [RequireBotPermission(GuildPermission.ManageChannels)]
        public async Task CleanupServerUltraAsync()
        {
            try
            {
                if (Context.Guild.OwnerId != Context.User.Id)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Forbidden", "Only the server owner can run this command."));
                    return;
                }

                var warning = EmbedFactory.BuildWarning(
                    "Destructive Action",
                    "This will delete **all channels and categories**.\n\n" +
                    "To confirm, type exactly:\n" +
                    "`CONFIRM DELETE ALL CHANNELS`\n\n" +
                    "You have 30 seconds.");

                await ReplyAsync(embed: warning);

                var confirmMsg = await NextMessageAsync(TimeSpan.FromSeconds(30));
                if (confirmMsg == null || !confirmMsg.Content.Trim().Equals("CONFIRM DELETE ALL CHANNELS", StringComparison.Ordinal))
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Cancelled", "Confirmation phrase not received. No changes were made."));
                    return;
                }

                await ReplyAsync(embed: EmbedFactory.BuildInfo("Cleanup Started", "Deleting all channels and categories."));

                int deletedChannels = 0;
                int deletedCategories = 0;
                int failed = 0;

                var allChannels = Context.Guild.Channels.ToList();

                foreach (var channel in allChannels.Where(c => c is not SocketCategoryChannel))
                {
                    try { await channel.DeleteAsync(); deletedChannels++; await Task.Delay(500); }
                    catch { failed++; }
                }

                foreach (var channel in allChannels.Where(c => c is SocketCategoryChannel))
                {
                    try { await channel.DeleteAsync(); deletedCategories++; await Task.Delay(500); }
                    catch { failed++; }
                }

                var done = EmbedFactory.BuildSuccess(
                    "Cleanup Complete",
                    $"Deleted Channels: {deletedChannels}\nDeleted Categories: {deletedCategories}\nFailed: {failed}");

                try { await Context.User.SendMessageAsync(embed: done); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Error", ex.Message));
            }
        }

        // -------------------------
        // Link/Invite blocking
        // -------------------------

        [Command("security-toggle-links")]
        [Alias("togglelinks")]
        [Summary("Toggle whether links are blocked (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ToggleBlockLinksAsync(bool enabled = true)
        {
            try
            {
                var cfg = SecurityService.GetConfig(Context.Guild.Id);
                if (!cfg.Enabled)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Not Enabled", "Security system is not enabled. Run `!setsecuritymod` first."));
                    return;
                }

                cfg.BlockLinks = enabled;
                SecurityService.SetConfig(Context.Guild.Id, cfg);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Link Blocking Updated",
                    $"Link blocking is now **{(enabled ? "ENABLED" : "DISABLED")}**\n\n" +
                    $"This will flag messages with links + suspicious keywords (nudes, porn, free nitro, etc.)"));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Failed", ex.Message));
            }
        }

        // -------------------------
        // Helper: NextMessageAsync
        // -------------------------

        private async Task<SocketMessage?> NextMessageAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<SocketMessage>();
            var expectedUserId = Context.User.Id;
            var expectedChannelId = Context.Channel.Id;
            var client = Context.Client;

            Task Handler(SocketMessage message)
            {
                if (message.Channel.Id == expectedChannelId &&
                    message.Author.Id == expectedUserId &&
                    !message.Author.IsBot)
                {
                    tcs.TrySetResult(message);
                }
                return Task.CompletedTask;
            }

            client.MessageReceived += Handler;
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            client.MessageReceived -= Handler;

            return completed == tcs.Task ? await tcs.Task : null;
        }
    }
}
