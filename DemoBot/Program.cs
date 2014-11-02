using System;
using irc;
using System.Threading;

namespace DemoBot
{
    class Program
    {
        static void Main(string[] args)
        {
            new DemoBot();
        }
    }

    class DemoBot
    {
        IrcBot irc;

        public DemoBot()
        {
            irc = new IrcBot("demobot", "null", new System.Collections.Generic.List<string> { "RobbingHood" }, false);
            
            irc.OnMotdEvent += irc_OnMotdEvent;
            irc.OnJoinChannelEvent += irc_OnJoinChannelEvent;
            irc.OnDisconnectEvent += irc_OnDisconnectEvent;
            irc.OnConnectEvent += irc_OnConnectEvent;
            irc.OnAnyMessageEvent += irc_OnAnyMessageEvent;
            irc.OnNoticeEvent += irc_OnNoticeEvent;
            irc.OnLoginEvent += irc_OnLoginEvent;
            irc.OnEndOfMotdEvent += irc_OnEndOfMotdEvent;

            irc.Connect("irc.rizon.net", 6660);
        }

        void irc_OnEndOfMotdEvent()
        {
            Console.WriteLine("End of Motd");
            irc.JoinChannel("#demobot");
        }

        void irc_OnNoticeEvent(IrcMessage m)
        {
            Console.WriteLine(m.getMessage());
        }

        void irc_OnAnyMessageEvent(IrcMessage m)
        {
            Console.WriteLine(m.getMessage());
            if (m.getSenderName().Equals("RobbingHood") && m.getMessage().Equals("!re"))
            {
                irc.Disconnect();
                irc.ChangeNickOffline("demobot2");
                Thread.Sleep(1000);
                irc.Connect("irc.rizon.net", 6660);
                Thread.Sleep(1000);
                irc.Login();
            }
        }

        void irc_OnConnectEvent()
        {
            Console.WriteLine("Connected!");
        }

        void irc_OnDisconnectEvent()
        {
            Console.WriteLine("Disconnected!");
        }

        void irc_OnJoinChannelEvent(string channel)
        {
            Console.WriteLine("Joined channel " + channel);
        }

        void irc_OnMotdEvent(IrcMessage m)
        {
            //Console.WriteLine("motd: " + m.getMessage());
        }

        void irc_OnLoginEvent()
        {
            Console.WriteLine("Logged in!");
        }
    }
}
