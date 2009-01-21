using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using CachemanCommon;

namespace CachemanAPI {
    public class CachemanClient {


        private const int DEAD_SERVER_POLL_SECONDS = 120;
        private const int SERVER_CONNECT_TIMEOUT_SECONDS = 4;

        //
        // All these three arrays have related items at the same indices i.e
        // the the IPEndpoint for _netHelpers[i] will be at _servers[i] and if there's an entry in
        // _badServersCheck[i], it means it has been marked as a bad server
        //

        NetworkHelper[] _netHelpers;
        IPEndPoint[] _servers;
        DateTime[] _badServersCheck; //Time at which we can check the server

        public CachemanClient():this(new IPEndPoint(
                                                    Dns.GetHostAddresses(Dns.GetHostName())[0],
                                                    16180)) {
        }

        public CachemanClient(IPEndPoint server):this(new IPEndPoint[]{server}) {

        }

        public CachemanClient(IPEndPoint[] servers) {

            _servers = servers;
            _badServersCheck = new DateTime[servers.Length];
            _netHelpers = new NetworkHelper[servers.Length];
           
        }

        public void Connect() {
            //
            // Helper connection method for explicitly testing connections. Not required when writing code
            // since GET/SET/DELETE all try and make connections implicitly
            //
            for (int i = 0; i < _servers.Length; i++) {
                GetConnectedNetworkHelper(i);
            }
        }

        
        private NetworkHelper GetConnectedNetworkHelper(int index) {

            if (_badServersCheck[index] != default(DateTime)) {
                //
                // This server has been marked as a bad server in the past - we need to check whether
                // enough time has elapsed for us to try it again
                //
                if (DateTime.Now.CompareTo(_badServersCheck[index]) < 0) {
                    //
                    // Not enough time has elapsed
                    //
                    return null;
                } else {
                    //
                    // Time for the next poll 
                    //
                    _badServersCheck[index] = default(DateTime);
                }
            }

            if (_netHelpers[index] == null) {
                Socket socket = new Socket(_servers[index].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                IAsyncResult result = socket.BeginConnect(_servers[index], null, null);

                if (!result.AsyncWaitHandle.WaitOne(SERVER_CONNECT_TIMEOUT_SECONDS * 1000, true)
                    || !socket.Connected) {
                    //
                    // Couldn't finish connecting to the server in time. mark it as bad and try again after some time
                    //
                    socket.Close();
                    HandleBadServer(index);
                    throw new Exception("Connect exception - could not connect to server at " + _servers[index].ToString());
                }
                _netHelpers[index] = new NetworkHelper(socket, new byte[8194]);
            }

            return _netHelpers[index];
        }

        private int GetHashedServerIndex(string key) {

            //
            // Simple 64 bit Fowler-Noll-Vo hash implementation. 
            // TODO: Check distribution - http://bretm.home.comcast.net/hash/6.html and http://blogs.msdn.com/bclteam/archive/2003/10/31/49719.aspx
            // seems to suggest distribution problems in certain cases but it seems to good enough for most cases
            //
            ulong offset = 14695981039346656037;
            ulong prime = 1099511628211;

            ulong hash = offset;
            for (int i = 0; i < key.Length; i++) {
                hash *= prime;
                hash ^= key[i];
            }


            int index = (int)(  hash % (ulong) _netHelpers.Length);
            return index; 
        }

        
        private string NormalizeKey(string key) {
            //
            //Convert all invalid characters in the key
            // TODO: Come up with a better way to do this as someone might specify a key with nulls in it
            //
            return key.Replace(' ', (char)0);
        }

        private void  HandleBadServer(int index) {

            //
            //Something went wrong - force reconnect of client
            //after marking server bad
            //
            _netHelpers[index] = null;
            _badServersCheck[index] = DateTime.Now.AddSeconds(DEAD_SERVER_POLL_SECONDS);
        }
        public bool Set(string key, object obj, int ttl) {

            try {
                byte[] data;
                //
                // Serialize the object to set into a byte array
                //
                using (MemoryStream memStream = new MemoryStream()) {
                    new BinaryFormatter().Serialize(memStream, obj);
                    data = memStream.ToArray();
                }

                //
                // Get the correct key and server index to talk to
                //
                key = NormalizeKey(key);
                int index = GetHashedServerIndex(key);

                //
                // Get the NetworkHelper for that server (checks whether the server is currently marked as bad)
                //
                NetworkHelper netHelper = GetConnectedNetworkHelper(index);
                if(netHelper == null) {
                    return false;
                }

                //
                // Send it over the wire and block until done or until we timeout.
                //
                netHelper.SendHeaderAndBody(Command.GetStringCommand(CommandType.SET, key, data.Length, ttl),
                                                 data);
                IAsyncResult ar = netHelper.BeginReadLine(null, null);
                string response = netHelper.EndReadLine(ar);

                return response == "STORED";
            } catch (Exception) {

                HandleBadServer(GetHashedServerIndex(key));
                throw;
            }
            

        }

        public object Get(string key) {

            try {

                //
                // Get the right key and server to talk to
                //
                Object result;
                key = NormalizeKey(key);
                int index = GetHashedServerIndex(key);

                NetworkHelper netHelper = GetConnectedNetworkHelper(index);
                if (netHelper == null) {
                    return null;
                }

                //
                // Send a get command to the server and block until we're done or until we timeout
                //
                netHelper.SendMessage(Command.GetStringCommand(CommandType.GET, key, 0, 0));

                IAsyncResult ar = netHelper.BeginReadLine(null, null);
                string response = netHelper.EndReadLine(ar);


                //
                // Check whether we got a valid value back
                //
                if (response == "NOT_FOUND") {
                    return null;
                }
                Command cmd = Command.ParseCommand(response);

                Debug.Assert(cmd.Action == CommandType.VALUE);
                if (cmd.Action != CommandType.VALUE) {
                    return null;
                }

                //
                // Try and read the value from server. Block until done. Note that
                // most times we dont block here since the server just sends the data in one blast - the data
                // is probably in our internal buffers already
                //
                ar = netHelper.BeginReadValue(cmd.Size, null, null);
                byte[] data = netHelper.EndReadValue(ar);


                //
                // Deserialize object.
                // TODO: Think about issues where you deserialize a different version of a type. See
                // http://msdn2.microsoft.com/en-us/magazine/cc188950.aspx
                //
                using (MemoryStream memStream = new MemoryStream(data, 0, data.Length)) {
                    result = new BinaryFormatter().Deserialize(memStream);
                }
                Debug.Assert(result != null);
                return result;
            } catch (Exception) {
                HandleBadServer(GetHashedServerIndex(key));
                throw;
            }
                
        }

        public bool Delete(string key) {

            try {
                //
                // Get the right key and server and send a delete command to the server
                //
                key = NormalizeKey(key);
                int index = GetHashedServerIndex(key);
                NetworkHelper netHelper = GetConnectedNetworkHelper(index);
                if (netHelper == null) {
                    return false;
                }
                netHelper.SendMessage(Command.GetStringCommand(CommandType.DELETE, key, 0, 0));
                IAsyncResult ar = netHelper.BeginReadLine(null, null);
                string response = netHelper.EndReadLine(ar);

                return response == "DELETED";
            } catch (Exception) {
                HandleBadServer(GetHashedServerIndex(key));
                throw;
            }


        }



    }
}
