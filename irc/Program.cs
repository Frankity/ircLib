using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace irc
{
    // TODO: exception bei reconnect, change OnJoin to passive (wait for incoming JOIN message with our name), store numeric replies in class?

    public class IrcBot
    {
        private TcpClient botSocket;
        private NetworkStream botStream;
        private StreamWriter writer;
        private StreamReader reader;
        private string name, ircMessage, channels, quitMsg, versionMsg;
        private List<string> owners;
        private static bool shouldShutdown = false, shouldReceive = true, connected = false;

        public delegate void IrcMessageDelegate(IrcMessage m);
        public event IrcMessageDelegate OnQueryMessageEvent;
        public event IrcMessageDelegate OnChannelMessageEvent;
        public event IrcMessageDelegate OnAnyMessageEvent;
        public event IrcMessageDelegate OnNoticeEvent;
        public event IrcMessageDelegate OnMotdEvent;

        public delegate void ChannelDelegate(string channel);
        public event ChannelDelegate OnJoinChannelEvent;
        public event ChannelDelegate OnPartChannelEvent;

        public delegate void ErrorDelegate(string errorMessage);
        public event ErrorDelegate OnErrorEvent;

        public delegate void TwoStringsDelegate(string sender, string command);
        public event TwoStringsDelegate OnActionEvent;
        public event TwoStringsDelegate OnCtcpResponseEvent;
        private event TwoStringsDelegate OnCtcpRequestEvent; // Can not be subscribed to from outside.

        public delegate void TopicDelegate(string channel, string topic);
        public event TopicDelegate OnTopicMessageEvent;
        public event TopicDelegate OnTopicNotSetMessageEvent;

        public delegate void NickChangeDelegate(string oldNick, string newNick);
        public event NickChangeDelegate OnNickChangeEvent;
        
        public delegate void UserlistDelegate(string channel, string[] users);
        public event UserlistDelegate OnNamereplyEvent;

        public delegate void VoidDelegate();
        public event VoidDelegate OnLoginEvent;
        public event VoidDelegate OnDisconnectEvent;
        public event VoidDelegate OnConnectEvent;
        public event VoidDelegate OnEndOfMotdEvent;
        

        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);
        public delegate bool HandlerRoutine(CtrlTypes CtrlType);
        public enum CtrlTypes { CTRL_C_EVENT = 0, CTRL_BREAK_EVENT, CTRL_CLOSE_EVENT, CTRL_LOGOFF_EVENT = 5, CTRL_SHUTDOWN_EVENT }
        /// <summary>
        /// Gets called on console application shutdown, sets the shouldShutdown variable to true
        /// </summary>
        /// <param name="ctrlType">The reason why the console application is shutting down</param>
        /// <returns>True to signalize the OS that the process can now be closed</returns>
        private static bool ConsoleCtrlCheck(CtrlTypes ctrlType)
        {
            shouldShutdown = true;
            shouldReceive = false;
            Thread.Sleep(100); // give the CheckShutdown() thread enough time to notice the changed variable
            return true; // let the program exit
        }


        // Constructors

        /// <summary>
        /// Full constructor
        /// </summary>
        /// <param name="server">Hostname of the IRC server</param>
        /// <param name="port">The port the IRCd is listening on</param>
        /// <param name="botname">The name this client will be using</param>
        /// <param name="channels">A comma-seperated list of channels (e.g.: "#channel1, #channel2". Spaces will be removed)</param>
        /// <param name="owners">A List of IRC nicknames that have full control over the bot</param>
        /// <param name="useIdent">When true, the bot will be listening for IDENT request on port 113 in a seperate thread for a short time after connecting</param>
        /// <param name="verbose">When true, additional information is written to the console</param>
        public IrcBot(string botname, string channels, List<string> owners, bool useIdent)
        {
            this.name = botname;
            this.channels = channels; // can be a single channel, a comma seperated list or null
            this.quitMsg = "Leaving";
            this.versionMsg = "Undefined version";
            this.owners = owners;

            if (IsWindows()) InitializePostexitHook();
        }
        /// <summary>
        /// Minimalistic constructor. Joining channels and adding owners is still possible via methods.
        /// </summary>
        /// <param name="server">Hostname of the IRC server</param>
        /// <param name="port">The port the IRCd is listening on</param>
        /// <param name="botname">The name this client will be using</param>
        public IrcBot(string botname)
        {
            this.name = botname;
            this.quitMsg = "Leaving";
            this.versionMsg = "Undefined version";

            if (IsWindows()) InitializePostexitHook();
        }


        // Postconstruction

        /// <summary>
        /// Check if the current OS is Win32
        /// </summary>
        /// <returns>True when the OS is Win32NT</returns>
        private bool IsWindows()
        {
            return (Environment.OSVersion.Platform == PlatformID.Win32NT);
        }
        /// <summary>
        /// Run CheckShutdown() in a seperate thread and register ConsoleCtrlCheck(CtrlTypes ctrlType) as the console shutdown callback
        /// </summary>
        private void InitializePostexitHook()
        {
            new Thread(CheckShutdown).Start(); // continously check if the user requests the console application to close
            SetConsoleCtrlHandler(new HandlerRoutine(ConsoleCtrlCheck), true); // register the custom function "ConsoleCtrlCheck" to handle close requests
        }
        /// <summary>
        /// If the user closes the console window, disconnect from the IRC network properly
        /// </summary>
        private void CheckShutdown()
        {
            while (true)
            {
                Thread.Sleep(40);
                if (shouldShutdown)
                {
                    Disconnect();
                    break;
                }
            }
        }


        // Networking core

        /// <summary>
        /// Connect to the IRC server. Make sure to not call this again while being connected to a server.
        /// use Disconnect() first if you want to connect to another server.
        /// </summary>
        /// <param name="server">Hostname of the IRC server</param>
        /// <param name="port">The port the IRCd is listening on</param>
        public void Connect(string server, int port)
        {
            IdentListener identListener = new IdentListener(name);
            new Thread(identListener.Listen).Start();

            this.botSocket = new TcpClient(server, port);
            this.botStream = botSocket.GetStream();
            this.writer = new StreamWriter(botStream);
            this.writer.AutoFlush = true;
            this.writer.NewLine = "\r\n";  // no effect
            this.reader = new StreamReader(botStream);

            shouldReceive = true;
            connected = true;
            OnConnect();

            new Thread(Receive).Start();
            OnCtcpRequestEvent += HandleCtcpRequest; // hardcoded, only the replies can be changed (e.g. versionMsg)
            Login();
        }
        /// <summary>
        /// Send a raw IRC command to the IRCd
        /// </summary>
        /// <param name="message">The raw command to send</param>
        public void SendRaw(string message)
        {
            writer.WriteLine(message);
            writer.Flush();
        }
        /// <summary>
        /// Send a text message to either a channel or a user
        /// </summary>
        /// <param name="destination">The channel or the nickname of the recipient</param>
        /// <param name="message">The text message to send</param>
        public void Send(string destination, string message)
        {
            writer.WriteLine("PRIVMSG " + destination + " :" + message);
            writer.Flush();
        }
        /// <summary>
        /// Sends a CTCP request (e.g.: PING, VERSION) to another user
        /// </summary>
        /// <param name="destination">The nickname of the recipient</param>
        /// <param name="message">The CTCP command</param>
        public void SendCtcpRequest(string user, string message)
        {
            writer.WriteLine("PRIVMSG " + user + " :" + '\x01' + message + '\x01');
            writer.Flush();
        }
        /// <summary>
        /// Replies to a CTCP request
        /// </summary>
        /// <param name="user">The nickname of the user who sent the command</param>
        /// <param name="message">The CTCP response (e.g. VERSION 0.1b Windows)</param>
        private void SendCtcpResponse(string user, string message)
        {
            writer.WriteLine("NOTICE " + user + " :" + '\x01' + message + '\x01');
            writer.Flush();
        }
        /// <summary>
        /// Send an ACTION to a channel/client. Clients will usually append the sender's name to the message.
        /// The action "shrugs" will be displayed like "client shrugs" on other users' clients.
        /// </summary>
        /// <param name="destination">The channel/user to send the ACTION to</param>
        /// <param name="message">The action</param>
        public void SendAction(string destination, string message)
        {
            SendCtcpRequest(destination, "ACTION " + message);
        }
        /// <summary>
        /// Runs in a seperate thread. Receives, filters and converts incoming data and forwards relevant messages to HandleMessage(IrcMessge m)
        /// </summary>
        private void Receive()
        {
            try
            {
                while ((ircMessage = reader.ReadLine()) != null && shouldReceive)
                {
                    if (ircMessage.ToLower().StartsWith("ping")) Pong(ircMessage.Replace(ircMessage.Substring(0, 5), "")); // reply to PING with the given payload
                    else if (ircMessage.ToLower().StartsWith("error")) { OnErrorMessage(ircMessage); }
                    else DistributeIrcMessage(ParseIrcMessage(ircMessage));
                }
            }
            catch (IOException) { };
        }

        /// <summary>
        /// Parses a raw IRC message to an IrcMessage object.
        /// Code taken from Caleb Delnay.
        /// </summary>
        /// <param name="ircMessage"></param>
        /// <returns>The parsed message in the form of an IrcMessage object</returns>
        private IrcMessage ParseIrcMessage(string ircMessage)
        {
            int prefixEnd = -1, trailingStart = ircMessage.Length;
            string trailing = String.Empty; // Usually the actual text message
            string prefix = String.Empty; // The sender (complete user adress or server)
            string command = String.Empty; // IRC command (PRIVMSG, NOTICE, etc.)
            string[] parameters = null; // Usually the destination (channel/user) plus in some cases additional parameters (e.g. for RPL_NAMEREPLY: "destination = #joined_channel")
            
            // Grab the prefix if it is present. If a message begins
            // with a colon, the characters following the colon until
            // the first space are the prefix.
            if (ircMessage.StartsWith(":"))
            {
                prefixEnd = ircMessage.IndexOf(" ");
                prefix = ircMessage.Substring(1, prefixEnd - 1);
            }
 
            // Grab the trailing if it is present. If a message contains
            // a space immediately following a colon, all characters after
            // the colon are the trailing part.
            trailingStart = ircMessage.IndexOf(" :");
            if (trailingStart >= 0) trailing = ircMessage.Substring(trailingStart + 2);
            else trailingStart = ircMessage.Length;
 
            // Use the prefix end position and trailing part start
            // position to extract the command and parameters.
            string[] commandAndParameters = ircMessage.Substring(prefixEnd + 1, trailingStart - prefixEnd - 1).Split(' ');
 
            // The command will always be the first element of the array.
            command = commandAndParameters[0];
 
            // The rest of the elements are the parameters, if they exist.
            // Skip the first element because that is the command.
            if (commandAndParameters.Length > 1)
            {
                parameters = new string[commandAndParameters.Length - 1];
                for (int n = 0; n < commandAndParameters.Length - 1; n++)
                {
                    parameters[n] = commandAndParameters[n + 1];
                }
            }
                
 
            // If the trailing part is valid add the trailing part to the
            // end of the parameters. 
            // Disabled because the last parameter stays seperate.
            /*if (!String.IsNullOrEmpty(trailing))
            {
                parameters = parameters.Concat(new string[] { trailing }).ToArray();
            }*/

            return new IrcMessage(prefix, command, parameters, trailing);
            
        }

        /// <summary>
        /// Depending on it's type (Channel message, private message, notice, raw IRC message, CTCP command), forward the incoming message to the correct handler function.
        /// </summary>
        /// <param name="m"><see cref="IrcMessage"/> object that contains all necessary information (such as the sender, the message type, the actual text, etc.)</param>
        private void DistributeIrcMessage(IrcMessage m)
        {
            if (m.getCommand().ToLower().Equals("privmsg"))
            {
                HandlePrivmsg(m);
            }
            else if (m.getCommand().ToLower().Equals("notice"))
            {
                HandleNoticeAndCtcp(m);
            }
            else if (m.getCommand().ToLower().Equals("nick"))
            {
                HandleNickMessage(m);
            }
            else if (isNumericIrcResponse(m.getCommand()))
            {
                HandleNumericMessage(m);
            }
        }
        private void HandlePrivmsg(IrcMessage m)
        {
            if (m.getMessage().Substring(0, 1).ToCharArray()[0].Equals('\x01')) // if first character equals '\x01', it's a CTCP request
            {
                string ctcpRequest = m.getMessage().Substring(1, m.getMessage().Length - 2); // remove start and end markers
                OnCtcpRequest(m.getSenderName(), ctcpRequest);
            }
            else if (m.getParameters()[0].Equals(name))
            {
                OnQueryMessage(m);
            }
            else
            {
                OnChannelMessage(m);
            }
            OnAnyMessage(m);
        }
        private void HandleNoticeAndCtcp(IrcMessage m)
        {
            if (m.getMessage().Substring(0, 1).ToCharArray()[0].Equals('\x01')) // if first character equals '\x01', it's a CTCP response
                {
                    string ctcpResponse = m.getMessage().Substring(1, m.getMessage().Length - 2); // remove start and end markers
                    OnCtcpResponse(m.getSenderName(), ctcpResponse);
                }
                else
                {
                    OnNotice(m);
                }
        }
        private void HandleNickMessage(IrcMessage m)
        {
            if (m.getSenderName().Equals(name)) name = m.getMessage();
            OnNickChange(m.getSenderName(), m.getMessage());
        }
        private void HandleNumericMessage(IrcMessage m)
        {
            switch (m.getCommand())
            {
                case "331":
                    OnTopicNotSetMessage(m);
                    break;
                case "332":
                    OnTopicMessage(m);
                    break;
                case "353":
                    OnNamereplyMessage(m);
                    break;
                case "372":
                    OnMotdMessage(m);
                    break;
                case "376": // End of MOTD
                    OnEndOfMotd();
                    JoinChannel(channels); // IRCd should be ready for JOIN commands after MOTD is finished
                    break;
                case "431":
                    OnErrorMessage("ERR_NONICKNAMEGIVEN");
                    break;
                case "432":
                    OnErrorMessage("ERR_ERRONEUSNICKNAME");
                    break;
                case "433":
                    OnErrorMessage("ERR_NICKNAMEINUSE");
                    break;
                case "436":
                    OnErrorMessage("ERR_NICKCOLLISION");
                    break;
                case "437":
                    OnErrorMessage("ERR_UNAVAILRESOURCE");
                    break;
                case "484":
                    OnErrorMessage("ERR_RESTRICTED");
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Responds to incoming CTCP requests, triggered by OnCtcpRequestEvent
        /// </summary>
        /// <param name="sender">The user who sent the request</param>
        /// <param name="ctcpRequest">The request that was sent</param>
        private void HandleCtcpRequest(string sender, string ctcpRequest)
        {
            if (ctcpRequest.ToLower().Equals("version"))
            {
                SendCtcpResponse(sender, "VERSION " + versionMsg + " " + Environment.OSVersion.VersionString);
            }
            else if (ctcpRequest.ToLower().Equals("time"))
            {
                SendCtcpResponse(sender, "TIME " + DateTime.Now.ToString());
            }
            else if (ctcpRequest.ToLower().Equals("ping"))
            {
                if (ctcpRequest.Contains(" ") && ctcpRequest.Replace("PING ", "").Length >= 1) // ping with payload to send back
                {
                    SendCtcpResponse(sender, "PING " + ctcpRequest.Split(' ')[1]);
                }
                else // blank ping
                {
                    SendCtcpResponse(sender, "PING");
                }
            }
            else if (ctcpRequest.ToLower().StartsWith("action"))
            {
                OnAction(sender, ctcpRequest.Replace(ctcpRequest.Substring(0, 7), ""));
            }
        }
        /// <summary>
        /// Disconnects from the IRCd and closes the socket
        /// </summary>
        public void Disconnect()
        {
            OnDisconnect();
            Quit();
            writer.Close();
            reader.Close();
            botStream.Close();
            botSocket.Close();
            connected = false;
        }
        

        // Event assigning

        private void OnConnect()
        {
            if (OnConnectEvent != null) OnConnectEvent();
        }
        private void OnDisconnect()
        {
            if (OnDisconnectEvent != null) OnDisconnectEvent();
        }
        private void OnLogin()
        {
            if (OnLoginEvent != null) { OnLoginEvent(); }
        }
        private void OnEndOfMotd()
        {
            if (OnEndOfMotdEvent != null) OnEndOfMotdEvent();
        }
        private void OnQueryMessage(IrcMessage m)
        {
            if (OnQueryMessageEvent != null) OnQueryMessageEvent(m);
        }
        private void OnChannelMessage(IrcMessage m)
        {
            if (OnChannelMessageEvent != null) OnChannelMessageEvent(m);
        }
        /// <summary>
        /// Gets called on any PRIVMSG the user receives. This includes queries, channel messages and CTCP requests.
        /// </summary>
        /// <param name="m">The IrcMessage to handle</param>
        private void OnAnyMessage(IrcMessage m)
        {
            if (OnAnyMessageEvent != null) OnAnyMessageEvent(m);
        }
        private void OnNotice(IrcMessage m)
        {
            if (OnNoticeEvent != null) OnNoticeEvent(m);
        }
        private void OnAction(string sender, string action)
        {
            if (OnActionEvent != null) OnActionEvent(sender, action);
        }
        private void OnMotdMessage(IrcMessage m)
        {
            if (OnMotdEvent != null) OnMotdEvent(m);
        }
        private void OnCtcpResponse(string sender, string ctcpResponse)
        {
            if (OnCtcpResponseEvent != null) OnCtcpResponseEvent(sender, ctcpResponse);
        }
        private void OnCtcpRequest(string sender, string ctcpResponse)
        {
            if (OnCtcpRequestEvent != null) OnCtcpRequestEvent(sender, ctcpResponse);
        }
        private void OnErrorMessage(string err)
        {
            if (OnErrorEvent != null) OnErrorEvent(err);
        }
        private void OnJoinChannel(string channel)
        {
            SendRaw("TOPIC " + channel);
            if (OnJoinChannelEvent != null) OnJoinChannelEvent(channel);
        }
        private void OnPartChannel(string channel)
        {
            if (OnPartChannelEvent != null) OnPartChannelEvent(channel);
        }
        private void OnNamereplyMessage(IrcMessage m)
        {
            if (m.getParameters().Length == 3 && m.getParameters()[2].StartsWith("#") && OnNamereplyEvent != null)
            {
                string[] users = m.getMessage().Split(' ');
                OnNamereplyEvent(m.getParameters()[2], users);
            }
        }
        private void OnTopicMessage(IrcMessage m)
        {
            if (m.getParameters().Length == 2 && OnTopicMessageEvent != null)
            {
                OnTopicMessageEvent(m.getParameters()[1], m.getMessage()); // Channel/topic
            }
        }
        private void OnTopicNotSetMessage(IrcMessage m)
        {
            if (m.getParameters().Length == 2 && OnTopicNotSetMessageEvent != null)
            {
                OnTopicNotSetMessageEvent(m.getParameters()[1], m.getMessage()); // Channel/"No topic is set"
            }
            
        }
        
        /// <summary>
        /// Triggers on both others' nicks and our nick.
        /// </summary>
        /// <param name="oldnick">The old nick</param>
        /// <param name="newnick">The new nick</param>
        private void OnNickChange(string oldnick, string newnick)
        {
            if (OnNickChangeEvent != null) OnNickChangeEvent(oldnick, newnick);
        }

        // Advanced networking

        /// <summary>
        /// Logs in to the IRCd
        /// </summary>
        public void Login()
        {
            SendRaw("NICK " + name);
            SendRaw("USER " + name + " 0 * :" + name);
            OnLogin();
        }
        /// <summary>
        /// Join one or more channels (comma-seperated string, spaces will be replaced)
        /// Example: "#channel1, #channel2"
        /// </summary>
        /// <param name="channel">A comma-seperated list of channels to join</param>
        public void JoinChannel(string channel)
        {
            if (channel != null)
            {
                SendRaw("JOIN " + channel); // the IRCd will detect if it's one or more channels
                channel = channel.Replace(" ", ""); // in case it's a list of channels
                if (channel.Contains(","))
                {
                    string[] channels = channel.Split(',');
                    foreach (string chan in channels)
                    {
                        OnJoinChannel(chan);
                    }
                }
                else { OnJoinChannel(channel); }
            }
        }
        /// <summary>
        /// Leave one or more channels (comma-seperated string, spaces will be replaced)
        /// Example: "#channel1, #channel2"
        /// </summary>
        /// <param name="channel">A comma-seperated list of channels to leave</param>
        public void LeaveChannel(string channel)
        {
            channel = channel.Replace(" ", "");
            if (channel.Contains(","))
            {
                string[] channels = channel.Split(',');
                foreach (string chan in channels)
                {
                    OnPartChannel(chan);
                }
            }
            else { OnPartChannel(channel); }
            SendRaw("PART " + channel); // the IRCd will detect if it's one or more channels
        }
        /// <summary>
        /// Disconnects the client from the IRCd
        /// </summary>
        public void Quit()
        {
            SendRaw("QUIT :" + quitMsg);
        }
        /// <summary>
        /// Sends a nick change request to the IRCd.
        /// The local variable "name" will only be changed once the server confirms the nick change with a NICK message.
        /// </summary>
        /// <param name="nick">The new nickname</param>
        public void ChangeNick(string nick)
        {
            if (connected) SendRaw("NICK " + nick);
        }
        /// <summary>
        /// Changes the variable "name" if the bot is not connected to any network.
        /// Used when reconnecting with a different nickname.
        /// </summary>
        /// <param name="nick">The new nickname</param>
        public void ChangeNickOffline(string nick)
        {
            if (!connected) name = nick;
        }
        /// <summary>
        /// Replies to a PING message in order to signalize the server that this client is still alive
        /// </summary>
        /// <param name="payload">A string appended to the "PING" sent by the IRCd which has to be sent back unmodified</param>
        private void Pong(string payload)
        {
            SendRaw("PONG " + payload);
        }


        // Getters, Setters, etc.

        /// <summary>
        /// Checks if a user is marked as an owner. Used to limit commands to specific users only.
        /// </summary>
        /// <param name="user">The user to check</param>
        /// <returns></returns>
        public bool isOwner(string user)
        {
            if (owners != null) return owners.Contains(user);
            else return false;
        }
        /// <summary>
        /// Add a new user to the List of users who are marked as owners
        /// </summary>
        /// <param name="owner">The user to add as an owner</param>
        public void AddOwner(string owner)
        {
            if (owners != null) owners.Add(owner);
            else owners = new List<string> { owner };
        }
        /// <summary>
        /// Remove a user from the List of users who are marked as owners
        /// </summary>
        /// <param name="owner">The user to remove from the List of owners</param>
        public void RemoveOwner(string owner)
        {
            if (owners != null) owners.Remove(owner);
            else OnErrorMessage("Cannot remove owner from empty List! (nullptr)");
        }
        /// <summary>
        /// Change/replace/remove the message that is shown the the channel when this client disconnects
        /// </summary>
        /// <param name="text">The new message</param>
        public void ChangeQuitMsg(string text)
        {
            quitMsg = text;
        }
        /// <summary>
        /// Change the message that is shown when a user sends a CTCP version request
        /// </summary>
        /// <param name="version">The new version</param>
        public void ChangeVersionMsg(string version)
        {
            versionMsg = version;
        }
        /// <summary>
        /// Check if an IRC "mode" is a numeric reply (Refernce: http://tools.ietf.org/html/rfc1459#section-6)
        /// </summary>
        /// <param name="text">An IRC "mode" to check (might be PRIVMSG as well as a number)</param>
        /// <returns>True when the mode is numeric</returns>
        private bool isNumericIrcResponse(string text)
        {
            int n;
            return int.TryParse(text, out n);
        }
        /// <summary>
        /// Will disconnect from the IRCd and stop all threads
        /// </summary>
        public void Shutdown()
        {
            shouldReceive = false; // Will stop the Receive() loop
            shouldShutdown = true; // Will trigger CheckShutdown() and therefore Disconnect()
        }
    }

    /// <summary>
    /// Represents any kind of IRC message that this client receives.
    /// More convenient than passing over multiple variables.
    /// </summary>
    public class IrcMessage
    {
        private String sender, senderName, command, message;
        private string[] parameters;

        public IrcMessage(String sender, String mode, String[] parameters, String message)
        {
            this.sender = sender; // hostname, e.g.: :nickname!username@host.provider.net
            this.senderName = sender.Split('!')[0].Replace(":", ""); // e.g.: nickname
            this.command = mode; // e.g.: PRIVMSG
            this.parameters = parameters;
            this.message = message.Replace(":", "");
        }

        /// <summary>
        /// Get the hostname of the client who sent the message
        /// </summary>
        /// <returns>The hostname of the sender, e.g.: :nickname!username@host.provider.net</returns>
        public string getSender() { return sender; }
        /// <summary>
        /// Get only the nickname of the client who sent the message
        /// </summary>
        /// <returns>The nickname of the sender, e.g.: nickname</returns>
        public string getSenderName() { return senderName; }
        /// <summary>
        /// Can be numerical, PRIVMSG, NOTICE, etc.
        /// </summary>
        /// <returns>The mode of the message, e.g.: PRIVMSG, NOTICE, 001</returns>
        public string getCommand() { return command; }
        /// <summary>
        /// Destination of the message
        /// </summary>
        /// <returns>A nickname or a channel</returns>
        public string[] getParameters() { return parameters; }
        /// <summary>
        /// Get the actual text of the message
        /// </summary>
        /// <returns>The actual message</returns>
        public string getMessage() { return message; }
    }

    /// <summary>
    /// Waits for an incoming ident request from the IRCd on port 113 and identifies the user.
    /// Identifying is optional and doesn't serve any real purpose.
    /// </summary>
    public class IdentListener
    {
        private TcpListener identSocket;
        private TcpClient identConnection;
        private NetworkStream identStream;
        private StreamReader identReader;
        private StreamWriter identWriter;
        private string identQuery, identReply, myIdentity;

        public delegate void IdentResult();
        public event IdentResult IdentFailed;
        public event IdentResult IdentSuccessful;

        public IdentListener(string myIdentity)
        {
            identSocket = new TcpListener(IPAddress.Any, 113);
            identSocket.Server.ReceiveTimeout = 200;
            this.myIdentity = myIdentity;
        }

        public void Listen()
        {
            try
            {
                identSocket.Start();

                using (identConnection = identSocket.AcceptTcpClient())
                using (identStream = identConnection.GetStream())
                using (identReader = new StreamReader(identStream))
                using (identWriter = new StreamWriter(identStream))
                {
                    identQuery = identReader.ReadLine();
                    identReply = identQuery + " : USERID : OTHER : " + myIdentity;
                    identWriter.WriteLine(identReply);
                    identWriter.Flush();
                    identReader.Close();
                    identWriter.Close();
                }
                identSocket.Stop();
                if (IdentSuccessful != null) IdentSuccessful();
            }

            catch (SocketException)
            {
                if (IdentFailed != null) IdentFailed();
            }
        }
    }
}