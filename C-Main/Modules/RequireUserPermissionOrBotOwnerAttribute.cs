using System;
using System.Threading.Tasks;
using Discord.Commands;
using Discord;

namespace MainbotCSharp.Modules
{
    public class RequireUserPermissionOrBotOwnerAttribute : PreconditionAttribute
    {
        private readonly GuildPermission _guildPermission;

        public RequireUserPermissionOrBotOwnerAttribute(GuildPermission guildPermission)
        {
            _guildPermission = guildPermission;
        }

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var user = context.User;

            // Check if user is a bot owner - bot owners have all permissions
            if (Bot.IsBotOwner(user.Id))
            {
                return Task.FromResult(PreconditionResult.FromSuccess());
            }

            // Check regular guild permissions
            if (context.Guild != null && user is IGuildUser guildUser)
            {
                if (guildUser.GuildPermissions.Has(_guildPermission))
                {
                    return Task.FromResult(PreconditionResult.FromSuccess());
                }
            }

            return Task.FromResult(PreconditionResult.FromError($"You need {_guildPermission} permission or be a bot owner to use this command."));
        }
    }

    // Alternative attribute for ManageMessages + Bot Owner
    public class RequireManageMessagesOrBotOwnerAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var user = context.User;

            // Check if user is a bot owner - bot owners have all permissions
            if (Bot.IsBotOwner(user.Id))
            {
                return Task.FromResult(PreconditionResult.FromSuccess());
            }

            // Check regular guild permissions
            if (context.Guild != null && user is IGuildUser guildUser)
            {
                if (guildUser.GuildPermissions.Has(GuildPermission.ManageMessages))
                {
                    return Task.FromResult(PreconditionResult.FromSuccess());
                }
            }

            return Task.FromResult(PreconditionResult.FromError("You need Manage Messages permission or be a bot owner to use this command."));
        }
    }
}