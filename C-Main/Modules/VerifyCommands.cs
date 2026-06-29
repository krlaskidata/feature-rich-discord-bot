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
    // Verify Service Classes
    public class VerifyConfigEntry
    {
        public ulong ChannelId { get; set; }
        public ulong RoleId { get; set; }
        public ulong? MessageId { get; set; }
        public ulong? LogChannelId { get; set; }
        public Dictionary<ulong, bool?> Snapshot { get; set; } = new Dictionary<ulong, bool?>();
        public Dictionary<ulong, DateTime> PendingVerifications { get; set; } = new Dictionary<ulong, DateTime>();
    }

    public static class VerifyService
    {
        private const string VERIFY_FILE = "main_verify_config.json";
        private static Dictionary<ulong, VerifyConfigEntry> _cfg = LoadVerifyConfig();

        private static Dictionary<ulong, VerifyConfigEntry> LoadVerifyConfig()
        {
            try
            {
                if (!File.Exists(VERIFY_FILE)) return new Dictionary<ulong, VerifyConfigEntry>();
                var txt = File.ReadAllText(VERIFY_FILE);
                var d = JsonSerializer.Deserialize<Dictionary<ulong, VerifyConfigEntry>>(txt);
                return d ?? new Dictionary<ulong, VerifyConfigEntry>();
            }
            catch
            {
                return new Dictionary<ulong, VerifyConfigEntry>();
            }
        }

        private static void SaveVerifyConfig()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_cfg, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(VERIFY_FILE, txt);
            }
            catch { }
        }

        public static VerifyConfigEntry? GetConfig(ulong guildId)
        {
            if (_cfg.TryGetValue(guildId, out var e)) return e; return null;
        }

        public static void SetConfig(ulong guildId, VerifyConfigEntry config)
        {
            _cfg[guildId] = config; SaveVerifyConfig();
        }

        public static void RemoveConfig(ulong guildId)
        {
            if (_cfg.ContainsKey(guildId)) { _cfg.Remove(guildId); SaveVerifyConfig(); }
        }

        public static async Task HandleVerifyButtonAsync(SocketMessageComponent component)
        {
            try
            {
                if (component.Data.CustomId != "verify_button") return;

                if (!component.GuildId.HasValue)
                {
                    await component.RespondAsync("❌ Server not found.", ephemeral: true);
                    return;
                }

                var config = GetConfig(component.GuildId.Value);
                if (config == null)
                {
                    await component.RespondAsync("❌ Verification system not configured.", ephemeral: true);
                    return;
                }

                var guild = (component.User as SocketGuildUser)?.Guild;
                var user = component.User as SocketGuildUser;

                if (guild == null || user == null)
                {
                    await component.RespondAsync("❌ Unable to access server information.", ephemeral: true);
                    return;
                }

                var role = guild.GetRole(config.RoleId);

                if (role == null)
                {
                    await component.RespondAsync("❌ Verification role not found.", ephemeral: true);
                    return;
                }

                if (user.Roles.Contains(role))
                {
                    await component.RespondAsync("✅ You are already verified!", ephemeral: true);
                    return;
                }

                if ((DateTime.UtcNow - user.CreatedAt.UtcDateTime).TotalDays < 7)
                {
                    await component.RespondAsync("❌ Your account must be at least 7 days old to verify.", ephemeral: true);
                    await LogVerificationAttempt(guild, user, "Account too new", false);
                    return;
                }

                await VerifyUser(component, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Verify button error: {ex.Message}");
                await component.RespondAsync("❌ Verification failed. Please try again.", ephemeral: true);
            }
        }

        private static async Task VerifyUser(SocketMessageComponent component, VerifyConfigEntry config)
        {
            try
            {
                var guild = (component.User as SocketGuildUser)?.Guild;
                var user = component.User as SocketGuildUser;

                if (guild == null || user == null) return;

                var role = guild.GetRole(config.RoleId);

                await user.AddRoleAsync(role);
                config.Snapshot[user.Id] = true;
                config.PendingVerifications[user.Id] = DateTime.UtcNow;
                SaveVerifyConfig();

                await component.RespondAsync($"✅ **Welcome to {guild.Name}!** You have been verified successfully!", ephemeral: true);
                await LogVerificationAttempt(guild, user, "Button verification", true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"User verification error: {ex.Message}");
                await component.RespondAsync("❌ Failed to verify. Please contact an administrator.", ephemeral: true);
            }
        }

        private static async Task LogVerificationAttempt(SocketGuild guild, SocketUser user, string method, bool success)
        {
            try
            {
                var config = GetConfig(guild.Id);
                if (config?.LogChannelId == null) return;

                var logChannel = guild.GetTextChannel(config.LogChannelId.Value);
                if (logChannel == null) return;

                var embed = new EmbedBuilder()
                    .WithTitle(success ? "✅ Verification Successful" : "❌ Verification Failed")
                    .WithColor(success ? Color.Green : Color.Red)
                    .AddField("User", $"{user.Mention} (`{user.Id}`)", true)
                    .AddField("Method", method, true)
                    .AddField("Account Age", $"{(DateTime.UtcNow - user.CreatedAt.UtcDateTime).TotalDays:F1} days", true)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

                await logChannel.SendMessageAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Verification log error: {ex.Message}");
            }
        }

        public static async Task<bool> ManualVerifyUser(SocketGuild guild, SocketUser user, SocketUser moderator)
        {
            try
            {
                var config = GetConfig(guild.Id);
                if (config == null) return false;

                var role = guild.GetRole(config.RoleId);
                if (role == null) return false;

                var guildUser = guild.GetUser(user.Id);
                if (guildUser == null) return false;

                await guildUser.AddRoleAsync(role);
                config.Snapshot[user.Id] = true;
                SaveVerifyConfig();

                await LogVerificationAttempt(guild, user, $"Manual verification by {moderator.Username}", true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> UnverifyUser(SocketGuild guild, SocketUser user, SocketUser moderator)
        {
            try
            {
                var config = GetConfig(guild.Id);
                if (config == null) return false;

                var role = guild.GetRole(config.RoleId);
                if (role == null) return false;

                var guildUser = guild.GetUser(user.Id);
                if (guildUser == null) return false;

                await guildUser.RemoveRoleAsync(role);
                config.Snapshot[user.Id] = false;
                SaveVerifyConfig();

                await LogVerificationAttempt(guild, user, $"Manual unverification by {moderator.Username}", false);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
    }

    public class VerifyCommands : ModuleBase<SocketCommandContext>
    {
        [Command("verify-setup")]
        [Summary("Setup verification system (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task SetupVerifyAsync(IRole role, ITextChannel? logChannel = null)
        {
            try
            {
                var embed = new EmbedBuilder()
                    .WithTitle("✅ Server Verification")
                    .WithDescription("**Welcome to the server!**\n\n" +
                                   "To access all channels and features, please verify yourself by clicking the button below.\n\n" +
                                   "**Verification Requirements:**\n" +
                                   "• Your Discord account must be at least 7 days old\n" +
                                   "• Click the verification button\n\n" +
                                   "If you have any issues, contact a staff member.")
                    .WithColor(0x40E0D0)
                    .WithFooter("🛡️ Anti-bot protection enabled")
                    .WithThumbnailUrl("https://imgur.com/aYh8OAq.png");

                var button = new ComponentBuilder()
                    .WithButton("✅ Verify Me!", "verify_button", ButtonStyle.Success, new Emoji("✅"));

                var message = await Context.Channel.SendMessageAsync(embed: embed.Build(), components: button.Build());

                var config = new VerifyConfigEntry
                {
                    ChannelId = Context.Channel.Id,
                    RoleId = role.Id,
                    MessageId = message.Id,
                    LogChannelId = logChannel?.Id
                };

                VerifyService.SetConfig(Context.Guild.Id, config);

                var setupEmbed = new EmbedBuilder()
                    .WithTitle("✅ Verification System Configured")
                    .WithColor(Color.Green)
                    .AddField("Verified Role", role.Mention, true)
                    .AddField("Log Channel", logChannel?.Mention ?? "None", true)
                    .AddField("Anti-bot Protection", "7+ day account age", false)
                    .WithDescription("Users can now verify using the button above!");

                await ReplyAsync(embed: setupEmbed.Build());
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Setup failed: {ex.Message}");
            }
        }

        [Command("verify")]
        [Summary("Manually verify a user (Staff only)")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task ManualVerifyAsync(SocketGuildUser user)
        {
            try
            {
                var config = VerifyService.GetConfig(Context.Guild.Id);
                if (config == null)
                {
                    await ReplyAsync("❌ Verification system not configured.");
                    return;
                }

                var role = Context.Guild.GetRole(config.RoleId);
                if (user.Roles.Contains(role))
                {
                    await ReplyAsync("❌ User is already verified.");
                    return;
                }

                var success = await VerifyService.ManualVerifyUser(Context.Guild, user, Context.User);
                if (success)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("✅ User Verified")
                        .WithColor(Color.Green)
                        .AddField("User", user.Mention, true)
                        .AddField("Verified By", Context.User.Mention, true)
                        .AddField("Method", "Manual verification", true)
                        .WithTimestamp(DateTimeOffset.UtcNow);

                    await ReplyAsync(embed: embed.Build());
                }
                else
                {
                    await ReplyAsync("❌ Failed to verify user.");
                }
            }
            catch
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Failed to Verify")
                    .WithDescription("Failed to verify user.")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }

        [Command("unverify")]
        [Summary("Remove verification from a user (Staff only)")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task UnverifyAsync(SocketGuildUser user)
        {
            try
            {
                var config = VerifyService.GetConfig(Context.Guild.Id);
                if (config == null)
                {
                    await ReplyAsync("❌ Verification system not configured.");
                    return;
                }

                var role = Context.Guild.GetRole(config.RoleId);
                if (!user.Roles.Contains(role))
                {
                    await ReplyAsync("❌ User is not verified.");
                    return;
                }

                var success = await VerifyService.UnverifyUser(Context.Guild, user, Context.User);
                if (success)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("❌ User Unverified")
                        .WithColor(Color.Orange)
                        .AddField("User", user.Mention, true)
                        .AddField("Unverified By", Context.User.Mention, true)
                        .AddField("Method", "Manual unverification", true)
                        .WithTimestamp(DateTimeOffset.UtcNow);

                    await ReplyAsync(embed: embed.Build());
                }
                else
                {
                    await ReplyAsync("❌ Failed to unverify user.");
                }
            }
            catch
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Failed to Unverify")
                    .WithDescription("Failed to unverify user.")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: errorEmbed);
            }
        }

        [Command("verify-status")]
        [Summary("Check verification system status (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task VerifyStatusAsync()
        {
            try
            {
                var config = VerifyService.GetConfig(Context.Guild.Id);
                if (config == null)
                {
                    await ReplyAsync("❌ Verification system not configured.");
                    return;
                }

                var role = Context.Guild.GetRole(config.RoleId);
                var channel = Context.Guild.GetTextChannel(config.ChannelId);
                var logChannel = config.LogChannelId.HasValue ? Context.Guild.GetTextChannel(config.LogChannelId.Value) : null;

                var verifiedCount = config.Snapshot.Count(s => s.Value == true);
                var totalMembers = Context.Guild.MemberCount;
                var verificationRate = totalMembers > 0 ? (verifiedCount * 100.0 / totalMembers) : 0;

                var embed = new EmbedBuilder()
                    .WithTitle("✅ Verification System Status")
                    .WithColor(0x40E0D0)
                    .AddField("Configuration",
                        $"**Channel:** {channel?.Mention ?? "Not found"}\n" +
                        $"**Role:** {role?.Mention ?? "Not found"}\n" +
                        $"**Log Channel:** {logChannel?.Mention ?? "None"}", false)
                    .AddField("Statistics",
                        $"**Verified Users:** {verifiedCount:N0}\n" +
                        $"**Total Members:** {totalMembers:N0}\n" +
                        $"**Verification Rate:** {verificationRate:F1}%", true)
                    .AddField("Security",
                        "**Account Age:** 7+ days required\n" +
                        "**Anti-bot:** Enabled\n" +
                        "**Method:** Verify button", true)
                    .WithTimestamp(DateTimeOffset.UtcNow);

                if (config.MessageId.HasValue)
                {
                    embed.AddField("Message", $"ID: {config.MessageId.Value}", true);
                }

                await ReplyAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to get status: {ex.Message}");
            }
        }

        [Command("verify-stats")]
        [Summary("Show detailed verification statistics (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task VerifyStatsAsync()
        {
            try
            {
                var config = VerifyService.GetConfig(Context.Guild.Id);
                if (config == null)
                {
                    await ReplyAsync("❌ Verification system not configured.");
                    return;
                }

                var recentVerifications = config.PendingVerifications
                    .Where(p => (DateTime.UtcNow - p.Value).TotalDays <= 7)
                    .OrderByDescending(p => p.Value)
                    .Take(10)
                    .ToList();

                var embed = new EmbedBuilder()
                    .WithTitle("📊 Verification Statistics")
                    .WithColor(0x40E0D0)
                    .AddField("Overview",
                        $"**Total Verified:** {config.Snapshot.Count(s => s.Value == true):N0}\n" +
                        $"**Failed Attempts:** {config.Snapshot.Count(s => s.Value == false):N0}\n" +
                        $"**This Week:** {recentVerifications.Count:N0}", false);

                if (recentVerifications.Any())
                {
                    var recentList = recentVerifications
                        .Take(5)
                        .Select(v => $"<@{v.Key}> - <t:{((DateTimeOffset)v.Value).ToUnixTimeSeconds()}:R>")
                        .ToList();

                    embed.AddField("Recent Verifications", string.Join("\n", recentList), false);
                }

                await ReplyAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to get stats: {ex.Message}");
            }
        }

        [Command("verify-reset")]
        [Summary("Reset verification system (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task ResetVerifyAsync()
        {
            try
            {
                var config = VerifyService.GetConfig(Context.Guild.Id);
                if (config == null)
                {
                    await ReplyAsync("❌ Verification system not configured.");
                    return;
                }

                // Clear all verification data
                config.Snapshot.Clear();
                config.PendingVerifications.Clear();
                VerifyService.SetConfig(Context.Guild.Id, config);

                var embed = new EmbedBuilder()
                    .WithTitle("🔄 Verification System Reset")
                    .WithColor(Color.Orange)
                    .WithDescription("All verification data has been cleared.\n" +
                                   "**Note:** Users will keep their roles, but verification tracking has been reset.")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                await ReplyAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to reset: {ex.Message}");
            }
        }

        [Command("verify-remove")]
        [Summary("Remove verification system (Admin only)")]
        [RequireUserPermissionOrBotOwner(GuildPermission.Administrator)]
        public async Task RemoveVerifyAsync()
        {
            try
            {
                VerifyService.RemoveConfig(Context.Guild.Id);

                var embed = new EmbedBuilder()
                    .WithTitle("🗑️ Verification System Removed")
                    .WithColor(Color.Red)
                    .WithDescription("Verification system has been completely removed.\n" +
                                   "**Note:** Existing roles will not be affected.")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                await ReplyAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to remove: {ex.Message}");
            }
        }
    }
}
