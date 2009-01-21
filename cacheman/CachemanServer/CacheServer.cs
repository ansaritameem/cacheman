using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using CachemanCommon;

namespace CachemanServer {
    class CacheServer {

        private int _port; //Port this server listens on
        private IPAddress _bindAddress; // Address this server is bound to
        private ManualResetEvent _acceptEvent; //Event signal when a socket is accepted
        private Socket _listenSocket; //Socket we use for binding on the host server

        public CacheServer ( int port, IPAddress bindAddress ) {
            _port = port;
            _bindAddress = bindAddress;
            _acceptEvent = new ManualResetEvent(false);
            PerfCounters.InitializeCounters(bindAddress.ToString() + ":" + port.ToString());
        }


        public void StartListening () {
            //
            //Create a local endpoint using the IP address and the port
            //we're supposed to listen on. And then create a TCP/IP socket
            //

            IPEndPoint localEndPoint = new IPEndPoint(_bindAddress, _port);
            _listenSocket = new Socket(_bindAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            //
            // Bind the endpoint and listen for incoming connections. 
            //     
            
            _listenSocket.Bind(localEndPoint);
            _listenSocket.Listen(( int )SocketOptionName.MaxConnections);
            Log.LogMessage("Listening on " + localEndPoint.Address.ToString() + ":" + localEndPoint.Port.ToString());
            Log.LogMessage("Max cache size (in bytes):" + Global.MAX_CACHE_MEMORY.ToString()); 
            
            try{
                while (true) {
                    //
                    //  Fire off an async socket to listen for incoming connections. 
                    //
                    _acceptEvent.Reset();
                    _listenSocket.BeginAccept(new AsyncCallback(AcceptCallback), _listenSocket);
                    _acceptEvent.WaitOne();
                }
            } catch (SocketException ex) {
                //
                // Log any exception here. Don't take down the server as there
                // could be other sockets and threads still bound to them executing
                //
                Log.LogMessage(" StartListening " + ex.ToString());
            }
        }

        //
        // Tears down the main socket
        //
        public void StopListening() {

            _listenSocket.Close();
        }

        //
        // Async callback for socket accept event
        //

        public void AcceptCallback(IAsyncResult ar) {

            //
            // Free up the listen loop to go churn out more  sockets
            // 

            _acceptEvent.Set();

            //
            // Get client request socket and finish accept
            //
            byte[] buffer = new byte[Client.BufferSize];
            Socket listenSocket = (Socket)ar.AsyncState;
            Socket handler = null;
            try {
                handler = listenSocket.EndAccept(ar);
            } catch (SocketException ex) {
                handler.Shutdown(SocketShutdown.Both);
                Log.LogMessage(" AcceptCallback " + ex.ToString());
                return;
            }

            //
            // We have a new client. Create a new client object and kick off the state machine
            //
            
            ConfigureClientSocket(handler);
            Client currentClient = new Client {
                WorkSocket = handler,
                State = SocketState.ReadCommand,
                UniqueId = DateTime.Now.Ticks.ToString(),
                ClientNetworkHelper = new NetworkHelper(handler, buffer),
                Buffer = buffer
            };

            Log.LogMessage("Connected to client from " + handler.RemoteEndPoint.ToString() + " \n");
            StateMachine(currentClient);
        }

        // 
        // Changes settings on the socket we're using for talking to the client
        // N.B These changes are for both reading and writing
        //

        private static void ConfigureClientSocket(Socket clientSocket) {

            //
            // Turn off Nagle. We may send very small bits of data and more importantly, we cant
            // afford the 200ms timeout before the data is put on the wire
            //
            // N.B This means that every write is instantly put on the wire without being buffered
            // together. Make each write count!

            clientSocket.NoDelay = true;

            clientSocket.ReceiveTimeout = 2000;

            clientSocket.SendTimeout = 2000;

        }

        
        private void StateMachine(Client currentClient) {
            try {

                   switch (currentClient.State) {
                            
                            case SocketState.ReadCommand:
                                currentClient.ClientNetworkHelper.BeginReadLine( ReadCommandCallback , currentClient);
                                break;

                            case SocketState.ReadValue:

                                currentClient.ClientNetworkHelper.BeginReadValue(currentClient.ValueTotalBytes,ReadValueCallback, currentClient);
                                break;

                            default:

                                Debug.Assert(false);
                                Log.LogError("Server.StateMachine: Invalid state");

                                break;
                        }
                   
            } catch (Exception ex) {
                //
                // Something bad happened. 
                //
                Log.LogError("Exception in state machine (client will be disconnected!: " + ex.ToString());
                currentClient.Dispose();
            }
        }

        private void ReadCommandCallback(IAsyncResult ar) {

            Client currentClient = (Client)ar.AsyncState;

            try {
                currentClient.CurrentCommand = Command.ParseCommand(currentClient.ClientNetworkHelper.EndReadLine(ar));
            } catch (SocketException ex) {
                Log.LogMessage("Socket error with client - typically means client has shut down \n " + ex.ToString());
                currentClient.Dispose();
                return;
            } catch (Exception ex) {
                Log.LogError("Client will be shutdown due to exception in ReadCommandCallback. \n" + ex.ToString());
                currentClient.Dispose();
                return;
            }

            ExecCommand(currentClient);
            
            //
            //Done executing the current command - go back into the state machine
            //
            StateMachine(currentClient);


        }

        private void ReadValueCallback(IAsyncResult ar) {

            Client currentClient = (Client)ar.AsyncState;
            byte[] data;

            try {
                data = currentClient.ClientNetworkHelper.EndReadValue(ar);
            } catch (Exception ex) {
                //TODO - should we recover and continue client processing?
                
                Log.LogError("Client will be shutdown due to exception in ReadValueCallback \n" + ex.ToString());
                currentClient.Dispose();
                return;
            }

            
            // 
            // Store the data. If successfully stored, tell the client. If not, the SetValue call will send the error
            // message to the client
            //

            Store.SetValue(currentClient.CurrentCommand.Key,
                    data,
                    currentClient.CurrentCommand.TTL);
           currentClient.ClientNetworkHelper.SendMessage("STORED");
           PerfCounters.LogSet();

            currentClient.State = SocketState.ReadCommand;
            StateMachine(currentClient);


        }


        private void ExecCommand(Client currentClient) 
        {
            
            if (currentClient.CurrentCommand.Action == CommandType.GET) {

                //
                // Read value from the store and send it to the client if found. If we don't,
                // find it, send a NOT_FOUND message to the client. We then clean up the state machine
                // and wait for the next command from the client
                //

                byte[] data = Store.GetValue(currentClient.CurrentCommand.Key, currentClient);


                PerfCounters.LogGet(data != null);

                if (data != null) { 
                    currentClient.ClientNetworkHelper.SendValue(data , currentClient.CurrentCommand.Key);
                } else {                    
                    currentClient.ClientNetworkHelper.SendMessage( "NOT_FOUND");
                }
                return;
            }


            if (currentClient.CurrentCommand.Action == CommandType.DELETE) {
                //
                // Delete the specified value from the store. 
                // TODO: Send an error if the value wasnt found in the store
                //
                if (Store.RemoveValue(currentClient.CurrentCommand.Key)) {
                    currentClient.ClientNetworkHelper.SendMessage("DELETED");
                } else {
                    currentClient.ClientNetworkHelper.SendMessage("NOT_FOUND");
                }
                PerfCounters.LogDelete();
                return;

            }


            if (currentClient.CurrentCommand.Action == CommandType.SET) {
                //
                // Read the header for a SET/UPDATE command. 
                //
                if (currentClient.CurrentCommand.Size > Global.MAX_CACHE_MEMORY) {

                    currentClient.ClientNetworkHelper.SendError( "Item too large");
                    return;
                }
                
                currentClient.ValueTotalBytes = currentClient.CurrentCommand.Size;
                currentClient.State = SocketState.ReadValue;
                return;
            }

        }

    }
}
