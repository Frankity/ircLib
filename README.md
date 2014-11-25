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

Function name  | Description
-------------- | --------------
public IrcBot(string botname, string channels)  | Constructor<br />_channels_ is a comma-seperated list of channels to join (e.g.: #chan1,#chan2)
public void Connect(string server, int port)  | Connect to an IRC server specified by IP and port
public void SendRaw(string command)  | Send a raw IRC command to the IRC server.<br /> Use this if a method that you need is not implemented by default.
public void Send(string destination, string message)  | Send a plain text message to either a channel or a user<br /> _destination_ can be a channel starting with "#" or a the nickname of a user
public void SendCtcpRequest(string user, string message)  | Send a CTCP request to a user<br /> _message_ must be the CTCP command (e.g.: TIME, VERSION)
public void SendAction(string destination, string message)  | Send an action command to either a channel or a user<br />_destination_ can be a channel starting with "#" or a the nickname of a user<br />_message_ can be any plain text<br />Example: If your nickname is John and the message is "looks around", most clients will display the action as "John looks around"
public void Login()  | Send the NICK and USER commands to the server in order to be able to chat/join channels/etc.
public void JoinChannel(string channel)  | Join _channel_ where _channel_ is a comma-seperated list of channels, each starting with a "#"<br />(e.g.: "#chan1,#chan2")
public void LeaveChannel(string channel)  | Leave channel, refer to JoinChannel(string channel)
public void Quit()  | Send the QUIT command to the server
public void ChangeNick(string nick)  | Request a nickchange on the server.<br />The local variable _name_ will only be updated if the server confirms the nickname change (to prevent nick collision)
public void ChangeNickOffline  | Use this to change the nick while being disconnected from the server. <br />To change the nick while connected, use ChangeNick(string nick)
public void ChangeQuitMsg(string text)  | Change the message that will be shown when you send the QUIT command via Quit()
public void ChangeVersionMsg(string version)  | Change the reply of the client to CTCP VERSION requests
public void Shutdown() | Diconnect from the server and stop all threads
