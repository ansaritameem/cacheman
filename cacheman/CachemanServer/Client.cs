using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Net.Sockets;
using System.Net;
using CachemanCommon;

namespace CachemanServer
{
    //
    // Encapsulates a client - data read so far, what state are we in respect to the client
    //
    public class Client:IDisposable
    {
        //
        //Client socket
        //
        public Socket WorkSocket = null;

        //
        // Network helper - should be wrapped around WorkSocket above
        //
        public NetworkHelper ClientNetworkHelper = null;

        //
        //Size of receive buffer
        //
        public const int BufferSize = 65536;

        //
        // Current command we're working on
        //
        public Command CurrentCommand=null;

        //
        //Receive Buffer. Used by sockets to read data
        //
        public byte[] Buffer = null;

        //
        //Received data
        //
        public MemoryStream DataStream = new MemoryStream();

        //
        // The below come into play when we're trying to read a value from the user
        // valueTotalBytes has the total byte count of the value we are trying to read
        // 
  
        public long ValueTotalBytes = 0;

        //
        // The state of the connection - whether we're reading a command or reading a value, etc
        //
        public SocketState State = SocketState.NewConnection;

        //
        // Unique id to identify the client - helps while debugging. Right now, it is a randomly assigned number
        //
        public string UniqueId;

        //
        // This makes sure we have atmost one read queued up per client (since multiple reads cause us weird issues with the non-paged pool
        //
        public bool PendingRead;


        public void Dispose() {

            WorkSocket.Shutdown(SocketShutdown.Both);
            DataStream.Close();
            //
            // TODO: Is it safe to call GC.SupressFinalize here? Do we need to bother?
            //

        }
        


    }

    //
    // What state is the client in when dealing with the particular client
    // 
    public enum SocketState
    {

        NewConnection, /* Socket hasnt started talking to the client yet - it has just been connected */
        ReadCommand, /* We are currently trying to read a command from the socket */
        ReadValue, /* We are trying to read a value from the client. */
    }

}
