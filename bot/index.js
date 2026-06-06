const { Client, GatewayIntentBits, Events } = require('discord.js');
require('dotenv').config({ path: require('path').join(__dirname, '..', '.env') });

const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
  ],
});

client.once(Events.ClientReady, (c) => {
  console.log(`Ready: ${c.user.tag}`);
});

// ヘルプチャンネルへの自動応答（今後ここに追加）
client.on(Events.MessageCreate, async (message) => {
  if (message.author.bot) return;
  // TODO: help auto-response
});

client.login(process.env.DISCORD_BOT_TOKEN);
