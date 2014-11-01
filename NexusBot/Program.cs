using irc;
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace NexusBot
{
    class Program
    {
        static void Main(string[] args)
        {
            //NexusBot bot1 = new NexusBot("irc.rizon.net", 6667, "nxsbt", "#btr1", new List<string> { "RobbingHood" }, false);
            NexusBot bot2 = new NexusBot("irc.quakenet.org", 6667, "nxsbt", "#juzftzfrztf", new List<string> { "RobbingHood" }, false);
        }

    }

    class NexusBot : IrcBot
    {
        public NexusBot(string server, int port, string botname, string channels, List<string> owners, bool useIdent) : base(server, port, botname, channels, owners, useIdent)
        {
            OnChannelMessageEvent += NexusBot_ChannelMessage;
            OnQueryMessageEvent += NexusBot_QueryMessage;
            OnNamereplyEvent += NexusBot_OnNamereplyEvent;
        }

        void NexusBot_OnNamereplyEvent(string channel, string[] users)
        {
            Console.WriteLine("There are " + users.Length + " users on channel " + channel);
        }

        private void NexusBot_ChannelMessage(IrcMessage m)
        {
            Console.WriteLine("{0}/<{1}>  {2}", m.getParameters()[0], m.getSenderName(), m.getMessage());

            if (m.getMessage().Equals("!ea")) new Thread(delegate() { showStats(m.getParameters()[0], "ea"); }).Start();
            else if (m.getMessage().Equals("!nxs")) new Thread(delegate() { showStats(m.getParameters()[0], "nexus"); }).Start();
            else if (m.getMessage().Equals("!compare")) new Thread(delegate() { compare(m.getParameters()[0]); }).Start();
        }

        private void NexusBot_QueryMessage(IrcMessage m)
        {
            Console.WriteLine("{0} whispers {1}", m.getSenderName(), m.getMessage());

            if (m.getMessage().Equals("!ea")) new Thread(delegate() { showStats(m.getSenderName(), "ea"); }).Start();
            else if (m.getMessage().Equals("!nxs")) new Thread(delegate() { showStats(m.getSenderName(), "nexus"); }).Start();
            else if (m.getMessage().Equals("!compare")) new Thread(delegate() { compare(m.getSenderName()); }).Start();

            else if (m.getMessage().StartsWith("!nick") && isOwner(m.getSenderName()) && m.getMessage().Split(' ').Length > 1) { ChangeNick(m.getMessage().Split(' ')[1]); }
            else if (m.getMessage().StartsWith("!join") && isOwner(m.getSenderName()) && m.getMessage().Split(' ').Length > 1) { JoinChannel(m.getMessage().Split(' ')[1]); }
            else if (m.getMessage().StartsWith("!part") && isOwner(m.getSenderName()) && m.getMessage().Split(' ').Length > 1) { LeaveChannel(m.getMessage().Split(' ')[1]); }
            else if (m.getMessage().StartsWith("!quit") && isOwner(m.getSenderName())) { Quit(); }

            else if (m.getMessage().Equals("!cycle")) { Disconnect(); Thread.Sleep(1000); InitializeNetwork(getServer(), getPort()); Thread.Sleep(1000); Login(); }

            else if (m.getMessage().Equals("!names")) { SendRaw("NAMES #idle"); }
        }


        private int[] getEAstats()
        {
            WebClient webClient = new WebClient();
            Stream apiStream = webClient.OpenRead("http://api.bfbcs.com/api/pc?globalstats");
            StreamReader apiReader = new StreamReader(apiStream);
            String apiContent = apiReader.ReadToEnd();
            var results = JsonConvert.DeserializeObject<dynamic>(apiContent);
            int servers = (results.s_pc.servers == null ? 0 : results.s_pc.servers);
            int players = (results.s_pc.players == null ? 0 : results.s_pc.players);
            int slots = (results.s_pc.slots == null ? 0 : results.s_pc.slots);
            int[] result = { servers, players, slots };
            return result;
        }

        private int[] getNXSstats()
        {
            WebClient webClient = new WebClient();
            Stream apiStream = webClient.OpenRead("http://api.emulatornexus.com/v1/rome/stats");
            StreamReader apiReader = new StreamReader(apiStream);
            String apiContent = apiReader.ReadToEnd();
            JsonSerializer serializer = new JsonSerializer();
            var results = JsonConvert.DeserializeObject<dynamic>(apiContent);
            int servers = (results.data.servers.active == null ? 0 : results.data.servers.active);
            int players = (results.data.players.online == null ? 0 : results.data.players.online);
            int ingamePlayers = (results.data.players.ingame == null ? 0 : results.data.players.ingame);
            int[] result = { servers, players, ingamePlayers };
            return result;
        }

        private void showStats(string channel, string providerLowercase)
        {
            int[] input = null;
            string provider = "", thirdParam = "";
            switch (providerLowercase)
            {
                case "ea":
                    input = getEAstats();
                    provider = "EA";
                    thirdParam = " Slots: ";
                    break;
                case "nexus":
                    input = getNXSstats();
                    provider = "Nexus";
                    thirdParam = " Ingame Players: ";
                    break;
            }
            Console.WriteLine("[" + provider + " Stats]" + " Servers: " + input[0] + " | Players:  " + input[1] + " | " + thirdParam + input[2]);
            Send(channel, "[" + provider + " Stats]" + " Servers: " + input[0] + " | Players:  " + input[1] + " | " + thirdParam + input[2]);
        }

        private void compare(string channel)
        {
            int[] nxs = getNXSstats();
            int[] ea = getEAstats();
            string message = "";
            if (nxs[1] >= ea[1]) //compare playercount
            {
                message += "Nexus has " + (nxs[1] - ea[1]) + " more players";
                if (nxs[0] >= ea[0]) message += " and " + (nxs[0] - ea[0]) + " more servers than EA"; //compare servercount
                else message += ", but " + (nxs[0] - ea[0]) + " servers less than EA";
            }
            else
            {
                message += "EA has " + (ea[1] - nxs[1]) + " more players";
                if (ea[0] >= nxs[0]) message += " and " + (ea[0] - nxs[0]) + " more servers than Nexus";
                else message += ", but " + (ea[0] - nxs[0]) + " servers less than Nexus";
            }

            Console.WriteLine("[COMPARE] " + message);
            Send(channel, message);
        }
    }
}
