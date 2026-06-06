const { Client, GatewayIntentBits, Events, MessageType } = require('discord.js');
require('dotenv').config({ path: require('path').join(__dirname, '..', '.env') });

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
