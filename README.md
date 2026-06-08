# ⚡ Code.Master() — Feature-Rich Discord Bot

> A powerful, fully free Discord bot built with **C#** and **Discord.NET** — featuring an economy, XP system, tickets, giveaways, voice management, verification, and more.

---

## ✅ Own the final Bot (24/7 online)
> You can copy the link and invite him in your Server.
> https://discord.com/oauth2/authorize?client_id=1435244593301159978&permissions=8&integration_type=0&scope=bot

Please read the [LICENSE](LICENSE) before you copy code or fork it.

---

## 🎮 Features & Commands

---

### 🔒 Security System
> ⚠️ **Work in progress** — active development, improvements coming.

Log-channel based security system with auto-cleanup and server protection tools.

```
!setsecuritymod           → Set up security (creates log channel, activates system)
!status                   → Show security system status
!disable                  → Disable security system
!cleanup-now              → Delete all messages in the current channel
!cleanup-intervall <ch> 2h  → Auto-delete messages in a channel every 2h
!delcleanup-intervall <ch>  → Remove auto-cleanup from a channel
```

---

### 🎫 Ticket System
Interactive ticket system with support roles and transcript generation.

```
!ticket-setup             → Interactive setup wizard
!set-support @role        → Set the support role for ticket management
!ticket-close             → Close the current ticket
!ticket-transcript        → Generate a full transcript of the ticket
!ticket-status            → Show current ticket system status
!del-ticket-system        → Remove ticket system configuration
```

---

### 🔊 Voice System
Dynamic voice channels — users create their own private/public rooms on join.

```
!voicesetup               → Full voice system setup
!create [category-id]     → Create a Join-to-Create channel
!voicename CoolRoom       → Rename your voice channel
!voicelimit 5             → Set a user limit (0 = unlimited)
!voiceprivate             → Make your channel private
!voicepublic              → Make your channel public
!voicestats               → View your voice activity statistics
!voice-cleanup            → (Admin) Remove all empty voice channels
```

---

### 📊 XP & Level System
202-tier military ranking system. XP is earned by chatting and being active in voice channels.

```
!run-xp-setup             → Set up the XP system (creates all roles)
!xp                       → View your current XP and rank
!xp @user                 → View another user's XP
!xp-give @user 500        → (Admin) Grant XP to a user
!set-rank-channel #ch     → Set channel for level-up notifications
!remove-xp-setup          → Disable the XP system
```

> Ranks range from **Recruit** to **Final Authority** with 202 levels total.

---

### ✅ Verification System
Gate your server with a verification step — optionally with CAPTCHA.

```
!verify-setup #channel @role         → Set up verification in a channel
!verify-setup #channel @role captcha → Enable CAPTCHA verification
```

> Members must verify before gaining access to the server.

---

### 🎉 Giveaway System
> 🚧 **In progress**

Create and manage giveaways with reaction-based entry.

```
!gcreate 1h 1 Discord Nitro   → Create a 1-hour giveaway with 1 winner
!gend <message-id>             → End a giveaway early
!greroll <message-id>          → Re-roll and pick new winners
!glist                         → List all active giveaways
```

> Users join by reacting with 🎉

---

## ➕ Further Features

### 🔔 Bump Reminders
Automated reminders to bump your server on [Disboard](https://disboard.org).

```
!bumpreminder on    → Enable bump reminders
!bumpreminder off   → Disable bump reminders
!bumpstatus         → Check reminder status
```

> Automatically reminds every **2 hours** when your server is ready to bump.

---

### 🛠️ Utilities

```
!ping                        → Check bot latency
!info                        → Show bot information
!sendit <msg-id> to <ch-id>  → Forward a message to another channel
```

---

### 🎂 Birthday System
Track and celebrate member birthdays automatically.

```
!birthdayset 25.12.1995       → Save your birthday
!mybirthdayinfo               → View your saved birthday
!birthdaychannel #channel     → (Admin) Set the birthday announcement channel
!birthdaylist                 → (Admin) View all saved birthdays sorted by date
!birthdayremove               → Remove your birthday
```

---

## 🎲 Extra Bot Games for Chat

### 🏦 Economy System
A full economy with wallet, bank, daily rewards, streaks, and more.

```
!daily              → Collect your daily credits
!balance            → View wallet & bank balance
!deposit 500        → Deposit money into the bank (safe from robbers)
!withdraw all       → Withdraw from bank
!pay @user 200      → Send money to another user
!rob @user          → Attempt to steal from a user's wallet (40% success rate)
```

---

### 🎰 Casino & Gambling
Gambling games with anti-addiction limits built in.

```
!coinflip 100 heads       → Bet on a coin flip
!dice 200 15 20           → Roll a 20-sided dice and guess the number
!slots 500                → Spin the slot machine with jackpot chance
```

---

## 📄 License

This project is licensed under the **MIT License** — see [LICENSE](LICENSE) for details.

---

<div align="center">
  Made with ❤️ · Built on <a href="https://github.com/discord-net/Discord.Net">Discord.NET</a>
</div>
