const { Client, GatewayIntentBits, Events, MessageType } = require('discord.js');
const http = require('http');
if (!process.env.DISCORD_BOT_TOKEN) {
  require('dotenv').config({ path: require('path').join(__dirname, '..', '.env') });
}

// Render の無料枠がスリープしないよう HTTP サーバーを立てる
http.createServer((_, res) => res.end('ok')).listen(process.env.PORT || 3000);

const HI_CHANNEL_ID = '1512877243897085983';

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

client.on(Events.MessageCreate, async (message) => {
  // 👋・hi チャンネルの入室メッセージに 👋 をリアクション
  if (
    message.channel.id === HI_CHANNEL_ID &&
    message.type === MessageType.GuildMemberJoin
  ) {
    try { await message.react('👋'); } catch {}
    return;
  }

  if (message.author.bot) return;
  // TODO: help auto-response
});

client.login(process.env.DISCORD_BOT_TOKEN);
