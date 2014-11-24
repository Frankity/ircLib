ircLib
======
This is a simple, event-driven IRC library written in C#.
It's as easy as adding the lib to your project, creating a new IrcBot object and subscribing to the events
you wish to deal with (such as channel messages, topic messages, channel joins, CTCP requests etc.).


Example: 
```C#
class DemoBot
{
  IrcBot irc;

  public DemoBot()
  {
      irc = new IrcBot("demobot", "#demochannel");
      
      irc.OnConnectEvent += irc_OnConnectEvent;  
      irc.OnMotdEvent += irc_OnMotdEvent;
      irc.OnJoinChannelEvent += irc_OnJoinChannelEvent;

      irc.Connect("irc.rizon.net", 6660);
  }
  
  private void irc_OnConnectEvent()
  {
      Console.WriteLine("Connected!");
  }
  
  private void irc_OnMotdEvent(IrcMessage m)
  {
      Console.WriteLine("motd: " + m.getMessage());
  }

  private void irc_OnJoinChannelEvent(string channel)
  {
      Console.WriteLine("Joined channel " + channel);
  }
}
```

As you can see, IRC messages are wrapped in an IrcMessage object that contains all parts of a normal message (refer to https://tools.ietf.org/html/rfc2812).
You can send regular messages as well as raw IRC commands, IRC actions and CTCP requests.
