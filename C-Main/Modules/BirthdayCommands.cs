using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MainbotCSharp.Modules
{
    public class BirthdayCommands : ModuleBase<SocketCommandContext>
    {
        [Command("birthdaychannel")]
        [Summary("Set or create birthday notification channel (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task BirthdayChannelAsync([Remainder] string? arg = null)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                var usageEmbed = new EmbedBuilder()
                    .WithTitle("❌ Missing Input")
                    .WithDescription("Please provide either a channel ID or use `!new-birthchan` to create a new channel.\n\n**Usage:**\n`!birthdaychannel CHANNEL_ID` - Use existing channel\n`!birthdaychannel !new-birthchan` - Create new channel")
                    .WithColor(0x40E0D0)
                    .Build();
                await ReplyAsync(embed: usageEmbed);
                return;
            }

            arg = arg.Trim();

            if (arg == "!new-birthchan")
            {
                try
                {
                    var newChannel = await Context.Guild.CreateTextChannelAsync("★-birthday-wishes");

                    BirthdayService.SetBirthdayChannel(Context.Guild.Id, newChannel.Id);

                    var embed = new EmbedBuilder()
                        .WithTitle("🎂 Birthday Channel Created!")
                        .WithColor(0xFFD700)
                        .AddField("Channel", newChannel.Mention, true)
                        .AddField("Status", "✅ Active", true)
                        .WithDescription("Birthday wishes will be sent here automatically!")
                        .WithTimestamp(DateTimeOffset.UtcNow);

                    await ReplyAsync(embed: embed.Build());
                    await newChannel.SendMessageAsync("🎉 This channel is now set up for birthday wishes!");
                }
                catch (Exception ex)
                {
                    await ReplyAsync($"❌ Failed to create birthday channel: {ex.Message}");
                }
            }
            else
            {
                // Try to parse as channel ID
                if (ulong.TryParse(arg, out ulong channelId))
                {
                    var channel = Context.Guild.GetTextChannel(channelId);
                    if (channel == null)
                    {
                        var notFoundEmbed = new EmbedBuilder()
                            .WithTitle("❌ Channel Not Found")
                            .WithDescription("Channel not found! Please provide a valid channel ID from this server.")
                            .WithColor(0x40E0D0)
                            .Build();
                        await ReplyAsync(embed: notFoundEmbed);
                        return;
                    }

                    BirthdayService.SetBirthdayChannel(Context.Guild.Id, channelId);

                    var embed = new EmbedBuilder()
                        .WithTitle("🎂 Birthday Channel Set!")
                        .WithColor(0xFFD700)
                        .AddField("Channel", channel.Mention, true)
                        .AddField("Status", "✅ Active", true)
                        .WithDescription("Birthday wishes will be sent here automatically!")
                        .WithTimestamp(DateTimeOffset.UtcNow);

                    await ReplyAsync(embed: embed.Build());
                }
                else
                {
                    await ReplyAsync("❌ Invalid channel ID! Please provide a valid numeric channel ID or use `!new-birthchan`.");
                }
            }
        }

        [Command("birthdayset")]
        [Summary("Set your birthday (format: dd.mm.yyyy)")]
        public async Task BirthdaySetAsync([Remainder] string? birthday = null)
        {
            try { await Context.Message.DeleteAsync(); } catch { }

            if (string.IsNullOrWhiteSpace(birthday))
            {
                var msg = await ReplyAsync("❌ Please provide your birthday in the format: `dd.mm.yyyy`\n**Example:** `!birthdayset 25.12.1995`");
                _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                return;
            }

            var data = BirthdayService.GetGuildBirthdayData(Context.Guild.Id);
            if (data == null || data.ChannelId == 0)
            {
                var msg = await ReplyAsync("❌ Birthday channel not set! An administrator needs to run `!birthdaychannel` first.");
                _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                return;
            }
            birthday = birthday.Replace('.', '/');

            // Additional validation before calling service
            if (!System.Text.RegularExpressions.Regex.IsMatch(birthday, @"^\d{2}\/\d{2}\/\d{4}$"))
            {
                var msg = await ReplyAsync("❌ Invalid date format! Please use: `dd.mm.yyyy`\n**Example:** `25.12.1995`");
                _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                return;
            }
            try
            {
                var parts = birthday.Split('/');
                var day = int.Parse(parts[0]);
                var month = int.Parse(parts[1]);
                var year = int.Parse(parts[2]);

                if (day < 1 || day > 31)
                {
                    var msg = await ReplyAsync("❌ Invalid day! Please use a day between 1-31.");
                    _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                    return;
                }

                if (month < 1 || month > 12)
                {
                    var msg = await ReplyAsync("❌ Invalid month! Please use a month between 1-12.");
                    _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                    return;
                }

                if (year < 1900 || year > DateTime.Now.Year)
                {
                    var msg = await ReplyAsync($"❌ Invalid year! Please use a year between 1900-{DateTime.Now.Year}.");
                    _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                    return;
                }
                new DateTime(year, month, day);
            }
            catch
            {
                var msg = await ReplyAsync("❌ Invalid date! Please make sure the date exists (e.g., no February 30th).");
                _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                return;
            }

            if (BirthdayService.SetUserBirthday(Context.Guild.Id, Context.User.Id, birthday))
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🎂 Birthday Saved!")
                    .WithColor(0xFFD700)
                    .AddField("User", Context.User.Mention, true)
                    .AddField("Birthday", birthday.Substring(0, 5), true) // Only show dd/MM for privacy
                    .WithDescription("Your birthday has been saved! You'll receive wishes on your special day! 🎉")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                var reply = await ReplyAsync(embed: embed.Build());

                _ = Task.Delay(15000).ContinueWith(async _ => { try { await reply.DeleteAsync(); } catch { } });
            }
            else
            {
                var msg = await ReplyAsync("❌ Invalid date format! Please use: `dd.mm.yyyy`\n**Example:** `25.12.1995`");
                _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
            }
        }

        [Command("birthdaylist")]
        [Summary("Show all birthdays in the server sorted by date")]
        public async Task BirthdayListAsync()
        {
            var data = BirthdayService.GetGuildBirthdayData(Context.Guild.Id);
            if (data == null || !data.Users.Any())
            {
                await ReplyAsync("❌ No birthdays have been set yet! Use `!birthdayset dd.mm.yyyy` to add yours.");
                return;
            }

            try
            {
                var sortedBirthdays = data.Users
                    .Select(kvp => new
                    {
                        UserId = kvp.Key,
                        Birthday = kvp.Value,
                        Parts = kvp.Value.Split('/'),
                    })
                    .Select(x => new
                    {
                        x.UserId,
                        x.Birthday,
                        Day = int.Parse(x.Parts[0]),
                        Month = int.Parse(x.Parts[1]),
                        Year = x.Parts.Length > 2 ? x.Parts[2] : "????"
                    })
                    .OrderBy(x => x.Month)
                    .ThenBy(x => x.Day)
                    .ToList();

                var embed = new EmbedBuilder()
                    .WithTitle($"🎂 {Context.Guild.Name} - Birthday List")
                    .WithColor(0xFFD700)
                    .WithDescription($"**{sortedBirthdays.Count}** members with birthdays registered")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                // Group by month for better readability
                var monthNames = new[] { "", "January", "February", "March", "April", "May", "June",
                                        "July", "August", "September", "October", "November", "December" };

                var grouped = sortedBirthdays.GroupBy(x => x.Month);

                foreach (var monthGroup in grouped)
                {
                    var monthName = monthNames[monthGroup.Key];
                    var birthdayList = string.Join("\n", monthGroup.Select(b =>
                    {
                        var user = Context.Guild.GetUser(b.UserId);
                        string userName;
                        
                        if (user != null)
                        {
                            userName = user.Username;
                        }
                        else
                        {
                            // Try to get display name from user mention if possible
                            userName = $"<@{b.UserId}>";
                        }
                        
                        return $"🎈 **{b.Day:D2}/{b.Month:D2}** - {userName}";
                    }));

                    embed.AddField($"📅 {monthName}", birthdayList, false);
                }

                embed.WithFooter($"Use !birthdayset dd.mm.yyyy to add your birthday | Channel: #{Context.Guild.GetTextChannel(data.ChannelId)?.Name ?? "Not set"}");

                await ReplyAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Error displaying birthday list: {ex.Message}");
            }
        }

        [Command("birthdayremove")]
        [Alias("mybirthdayremove")]
        [Summary("Remove your birthday from the system")]
        public async Task BirthdayRemoveAsync()
        {
            if (BirthdayService.RemoveUserBirthday(Context.Guild.Id, Context.User.Id))
            {
                await ReplyAsync($"✅ {Context.User.Mention}, your birthday has been removed from the system.");
            }
            else
            {
                await ReplyAsync("❌ You don't have a birthday set in this server.");
            }
        }

        [Command("mybirthdayinfo")]
        [Summary("Show your saved birthday info")]
        public async Task MyBirthdayInfoAsync()
        {
            var data = BirthdayService.GetGuildBirthdayData(Context.Guild.Id);
            if (data == null || !data.Users.TryGetValue(Context.User.Id, out var birthday))
            {
                await ReplyAsync("❌ You haven't set your birthday yet. Use `!birthdayset dd.mm.yyyy`");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("🎂 Your Birthday Info")
                .WithColor(0xFFD700)
                .AddField("User", Context.User.Mention, true)
                .AddField("Birthday", birthday.Substring(0, 5), true) // Only show dd/MM
                .AddField("Channel", $"<#{data.ChannelId}>", true)
                .WithDescription("Your birthday is saved and you'll receive wishes on your special day!")
                .WithTimestamp(DateTimeOffset.UtcNow);

            await ReplyAsync(embed: embed.Build());
        }

        [Command("birthdaysetuser")]
        [Alias("setuserbday")]
        [Summary("Set another user's birthday (Moderator only)")]
        [RequireUserPermission(GuildPermission.KickMembers)]
        public async Task BirthdaySetUserAsync(SocketGuildUser targetUser, [Remainder] string? birthday = null)
        {
            await SetUserBirthdayInternal(targetUser.Id, targetUser.Username, birthday);
        }

        [Command("birthdaysetuserid")]
        [Alias("setuserbdayid")]
        [Summary("Set user's birthday by ID (Moderator only)")]
        [RequireUserPermission(GuildPermission.KickMembers)]
        public async Task BirthdaySetUserByIdAsync(ulong userId, [Remainder] string? birthday = null)
        {
            var user = Context.Guild.GetUser(userId);
            var username = user?.Username ?? $"User {userId}";
            await SetUserBirthdayInternal(userId, username, birthday);
        }

        private async Task SetUserBirthdayInternal(ulong targetUserId, string targetUsername, string? birthday)
        {
            // Delete command message for privacy
            try { await Context.Message.DeleteAsync(); } catch { }

            if (string.IsNullOrWhiteSpace(birthday))
            {
                var msg = await ReplyAsync($"❌ Please provide the birthday for {targetUsername} in the format: `dd.mm.yyyy`\n**Example:** `!birthdaysetuser @{targetUsername} 25.12.1995`");
                _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                return;
            }

            var data = BirthdayService.GetGuildBirthdayData(Context.Guild.Id);
            if (data == null || data.ChannelId == 0)
            {
                var msg = await ReplyAsync("❌ Birthday channel not set! An administrator needs to run `!birthdaychannel` first.");
                _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                return;
            }

            // Convert dd.mm.yyyy to dd/mm/yyyy for internal storage
            birthday = birthday.Replace('.', '/');

            // Additional validation before calling service
            if (!System.Text.RegularExpressions.Regex.IsMatch(birthday, @"^\d{2}\/\d{2}\/\d{4}$"))
            {
                var msg = await ReplyAsync("❌ Invalid date format! Please use: `dd.mm.yyyy`\n**Example:** `25.12.1995`");
                _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                return;
            }

            // Parse and validate the date components
            try
            {
                var parts = birthday.Split('/');
                var day = int.Parse(parts[0]);
                var month = int.Parse(parts[1]);
                var year = int.Parse(parts[2]);

                if (day < 1 || day > 31)
                {
                    var msg = await ReplyAsync("❌ Invalid day! Please use a day between 1-31.");
                    _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                    return;
                }

                if (month < 1 || month > 12)
                {
                    var msg = await ReplyAsync("❌ Invalid month! Please use a month between 1-12.");
                    _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                    return;
                }

                if (year < 1900 || year > DateTime.Now.Year)
                {
                    var msg = await ReplyAsync($"❌ Invalid year! Please use a year between 1900-{DateTime.Now.Year}.");
                    _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                    return;
                }

                // Try to create DateTime to validate
                new DateTime(year, month, day);
            }
            catch
            {
                var msg = await ReplyAsync("❌ Invalid date! Please make sure the date exists (e.g., no February 30th).");
                _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
                return;
            }

            if (BirthdayService.SetUserBirthday(Context.Guild.Id, targetUserId, birthday))
            {
                var targetUser = Context.Guild.GetUser(targetUserId);
                var userMention = targetUser?.Mention ?? $"<@{targetUserId}>";
                
                var embed = new EmbedBuilder()
                    .WithTitle("🎂 Birthday Set for User!")
                    .WithColor(0xFFD700)
                    .AddField("Target User", userMention, true)
                    .AddField("Birthday", birthday.Substring(0, 5), true) // Only show dd/MM for privacy
                    .AddField("Set by", Context.User.Mention, true)
                    .WithDescription($"{userMention}'s birthday has been saved! They'll receive wishes on their special day! 🎉")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                var reply = await ReplyAsync(embed: embed.Build());

                // Auto-delete confirmation after 15 seconds
                _ = Task.Delay(15000).ContinueWith(async _ => { try { await reply.DeleteAsync(); } catch { } });
            }
            else
            {
                var msg = await ReplyAsync("❌ Failed to save birthday! Please try again.");
                _ = Task.Delay(10000).ContinueWith(async _ => { try { await msg.DeleteAsync(); } catch { } });
            }
        }

        [Command("birthdayhelp")]
        [Alias("helpbday")]
        [Summary("Set another user's birthday (Trusted users)")]
        public async Task BirthdayHelpUserAsync(SocketGuildUser targetUser, [Remainder] string? birthday = null)
        {
            await SetUserBirthdayInternal(targetUser.Id, targetUser.Username, birthday);
        }

        [Command("birthdayremoveuser")]
        [Alias("removeuserbday")]
        [Summary("Remove another user's birthday (Moderator only)")]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        public async Task BirthdayRemoveUserAsync(SocketGuildUser targetUser)
        {
            if (BirthdayService.RemoveUserBirthday(Context.Guild.Id, targetUser.Id))
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🗑️ Birthday Removed")
                    .WithColor(0xFF6B6B)
                    .AddField("Target User", targetUser.Mention, true)
                    .AddField("Removed by", Context.User.Mention, true)
                    .WithDescription($"{targetUser.Mention}'s birthday has been removed from the system.")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                await ReplyAsync(embed: embed.Build());
            }
            else
            {
                await ReplyAsync($"❌ {targetUser.Mention} doesn't have a birthday set in this server.");
            }
        }

        [Command("birthdaydebug")]
        [Summary("Debug birthday system (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task BirthdayDebugAsync()
        {
            var data = BirthdayService.GetGuildBirthdayData(Context.Guild.Id);
            
            var embed = new EmbedBuilder()
                .WithTitle("🔧 Birthday System Debug")
                .WithColor(0x40E0D0)
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (data == null)
            {
                embed.AddField("Status", "❌ No birthday data found for this guild", false);
                embed.AddField("Solution", "Run `!birthdaychannel` to initialize the system", false);
            }
            else
            {
                embed.AddField("Channel ID", data.ChannelId.ToString(), true);
                embed.AddField("Users Count", data.Users.Count.ToString(), true);
                embed.AddField("Channel Status", 
                    Context.Guild.GetTextChannel(data.ChannelId) != null ? "✅ Valid" : "❌ Invalid", true);

                if (data.Users.Any())
                {
                    var userList = string.Join("\n", data.Users.Take(5).Select(kvp => 
                        $"<@{kvp.Key}>: {kvp.Value}"));
                    embed.AddField($"Recent Users ({Math.Min(5, data.Users.Count)} of {data.Users.Count})", userList, false);
                }
                else
                {
                    embed.AddField("Users", "No users have set birthdays yet", false);
                }
            }

            await ReplyAsync(embed: embed.Build());
        }
    }
}
