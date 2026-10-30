// Registers PeakMeter's slash commands with Discord (run once, and again after the command list changes).
// Needs the bot token from the Discord developer portal (Bot â†’ Reset Token). It stays on your PC: it is read from
// the environment and only sent to discord.com.
//   PowerShell:  $env:DISCORD_APP_ID="123..."; $env:DISCORD_BOT_TOKEN="..."; node discord/register.mjs
import { COMMANDS } from "../src/discord.mjs";

const app = process.env.DISCORD_APP_ID;
const token = process.env.DISCORD_BOT_TOKEN;
if (!app || !token) {
  console.error("Set DISCORD_APP_ID and DISCORD_BOT_TOKEN first.");
  process.exit(1);
}
const r = await fetch(`https://discord.com/api/v10/applications/${app}/commands`, {
  method: "PUT",
  headers: { "content-type": "application/json", authorization: `Bot ${token}` },
  body: JSON.stringify(COMMANDS),
});
const body = await r.json().catch(() => null);
if (!r.ok) {
  console.error("Discord said", r.status, JSON.stringify(body, null, 2));
  process.exit(1);
}
console.log("Registered:", body.map((c) => "/" + c.name).join(" "));
console.log(`Add the bot to a server: https://discord.com/oauth2/authorize?client_id=${app}&scope=applications.commands`);
