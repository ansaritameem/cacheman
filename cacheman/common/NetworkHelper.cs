using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;

namespace CachemanCommon {

    public enum NetworkOperation {
        ReadLine = 0,
        ReadValue = 1
    }
    public class NetworkHelper {

        Socket _socket;
        MemoryStream _dataStream;
        byte[] _buffer;


        public NetworkHelper(Socket remoteConnection, byte[] buffer) {

            _socket = remoteConnection;
            _dataStream = new MemoryStream();
            _buffer = buffer;            
            
        }

        public IAsyncResult BeginReadValue(long size, AsyncCallback callback, object state) {

           
            AsyncNetworkOperation asyncNetIO = new AsyncNetworkOperation(callback, state, NetworkOperation.ReadValue);
            asyncNetIO.ValueSizeToRead = size;
            //
            // Check whether we already have a command in our buffer
            //
            if (_dataStream.Length > 0) {

                byte[] possibleValue = TryReadValue(_dataStream, size);
                if (possibleValue != null) {
                    //
                    // We already have a command in our buffer. No need to fire off another read
                    asyncNetIO.SetCompleted(possibleValue, null);
                    return asyncNetIO;
                }
            } 
            _socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, ReadCallback, asyncNetIO);
            return asyncNetIO;


        }

        public byte[] EndReadValue(IAsyncResult ar) {

            return (byte[])((AsyncNetworkOperation)ar).EndInvoke();
        }

        private static byte[] TryReadValue(MemoryStream dataStream, long size) {
            if (dataStream.Position < size + 2) {
                //
                // We have more to read
                //
                return null;
            }

            byte[] value;

            dataStream.Seek(size, SeekOrigin.Begin);
            //
            // Check to see whether the last 2 bytes are \r\n. If they are, discard them and store the rest.
            //
            int shouldBeR = dataStream.ReadByte();
            int shouldBeN = dataStream.ReadByte();
            if (shouldBeR != (int)'\r' || shouldBeN != (int)'\n') {

                Debug.Assert(false);
                throw new Exception("Malformed data from other side " + ASCIIEncoding.ASCII.GetString(dataStream.ToArray()));
            } else {

                //
                // Make sure we ignore the final \r\n
                //
                value = new byte[size];
                dataStream.Seek(0, SeekOrigin.Begin);
                dataStream.Read(value, 0, (int)size);

            }

            // 
            // Finished reading value (or erroring out when trying to). If there's data we havent looked at yet, slide that over to the beginning as it'll probably
            // be the start of the next command
            //

            int lengthNotProcessed = (int)(dataStream.Length - (size + 2));
            Debug.Assert(lengthNotProcessed >= 0); // Should not be negative!
            byte[] allData = dataStream.ToArray();
            dataStream.Seek(0, SeekOrigin.Begin);
            dataStream.Write(allData, (int)size+ 2, lengthNotProcessed);
            dataStream.SetLength(lengthNotProcessed);
            allData = null;

            return value;


        }
        public IAsyncResult BeginReadLine(AsyncCallback callback, object state) {

            AsyncNetworkOperation asyncNetIO = new AsyncNetworkOperation(callback, state, NetworkOperation.ReadLine);
            //
            // Check whether we already have a command in our buffer
            //
            if (_dataStream.Length >0 ) {

                string possibleCommand = TryReadLine(_dataStream);
                if (possibleCommand != null) {
                    //
                    // We already have a command in our buffer. No need to fire off another read
                    asyncNetIO.SetCompleted(possibleCommand, null);
                    return asyncNetIO;
                }
            } 
            
            _socket.BeginReceive(_buffer,0,  _buffer.Length, SocketFlags.None, ReadCallback,asyncNetIO);
            return asyncNetIO;

        }

        private void ReadCallback(IAsyncResult ar) {

            AsyncNetworkOperation asyncNetIO = (AsyncNetworkOperation)ar.AsyncState;
            //
            // Try reading data from the socket. 
            //

            int bytesRead = 0;
            try {
                bytesRead = _socket.EndReceive(ar);


                if (bytesRead == 0) {
                    //
                    // Something went wrong - the client didn't give us any data. Shutdown the socket
                    //
                    _socket.Shutdown(SocketShutdown.Both);
                    asyncNetIO.SetCompleted(null, new Exception("Read 0 bytes from other side - socket shutdown"));
                    return;

                }

                _dataStream.Write(_buffer, 0, bytesRead);

                if (asyncNetIO.OperationType == NetworkOperation.ReadLine) {

                    string possibleCommand = TryReadLine(_dataStream);
                    if (possibleCommand != null) {
                        asyncNetIO.SetCompleted(possibleCommand, null);
                    } else {
                        _socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, ReadCallback, asyncNetIO);
                    }
                } else {
                    byte[] possibleValue = TryReadValue(_dataStream, asyncNetIO.ValueSizeToRead);
                    if (possibleValue != null) {
                        asyncNetIO.SetCompleted(possibleValue, null);
                    } else {
                        _socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, ReadCallback, asyncNetIO);
                    }

                }
            } catch (SocketException ex) {
                _socket.Shutdown(SocketShutdown.Both);
                asyncNetIO.SetCompleted(null, ex);
                Log.LogMessage("Connection closed \nDetails: " + ex.ToString());

            } catch (Exception ex) {
                //
                // Something bad happened - take down the current client
                //
                _socket.Shutdown(SocketShutdown.Both);
                asyncNetIO.SetCompleted(null, ex);
                Log.LogError("Exception in ReadCallback\nDetails: " + ex.ToString());

            }




        }

        public string EndReadLine(IAsyncResult ar) {

            return (string)((AsyncNetworkOperation)ar).EndInvoke();
        }



        //
        // Tries to read a line upto \r\n and returns true if successful.
        //Returns the line without the \r\n on success. Returns null if it cannot find a \r\n
        //
        private static string TryReadLine(MemoryStream dataStream) {

            // A command is just the first line terminated with a \r\n combo
            //   
            // Walk through the bytes read so far looking for a \r\n sequence
            // (13 and 10 respectively)
            //


            bool gotR = false; //Have we found \r yet
            bool gotN = false; // Have we found \n
            MemoryStream lineBuf = new MemoryStream(); // Line read so far
            
            //            
            //  Seek the stream to the beginning so that
            // we can read from the first byte
            //
            // N.B This means that for every return visit to this function (if the previous call didnt find \r\n,
            // we'll read the same bytes
            // since we always start at positionStart. Since lines (usually commands) can usually fit into one
            // ethernet frame, this is cheaper than doing the book-keeping to remember how much of
            // the command we read last time
            //
            
            dataStream.Seek(0, SeekOrigin.Begin);
            int currentByte = dataStream.ReadByte();

            while (currentByte >= 0) {

                lineBuf.WriteByte((byte)currentByte);

                gotR = (currentByte == 13); // Look for \r

                // 
                // Read the next byte. 
                // 1. If it is a \n, we're good - break out
                // 2. If it isn't, clear gotR and iterate again
                // If it is -1 (EOF), it'll be caught at the while condtion at the top
                //

                currentByte = dataStream.ReadByte();

                if (gotR) {
                    //
                    // Found \r - look for \n
                    //                  
                    if (currentByte == 10) {
                        gotN = true; // Found \n
                        break;
                    } else {
                        gotR = false; //Not \n - clear state and start all over again
                    }
                }
            }

            Debug.Assert(gotN == gotR, "Error : gotR and gotN should always be equal. This is a bug");

            //
            // Check whether we've found an entire line. If so, return it
            // and return to the state machine. 
            if (gotR && gotN) {

                //
                // Life is good - we found a \r\n. This means that lineBuf has the command with a trailing
                // \r. Convert the bytes into a string, strip the trailing \r and put it in the client object
                //     
                
                lineBuf.SetLength(lineBuf.Length - 1);
                string line = ASCIIEncoding.ASCII.GetString(lineBuf.ToArray());

                //
                // If there's any data we havent looked at, slide that over to the beginning as this could be the
                // start of the VALUE. This will let the value parsing code treat this data properly. If there's no data
                // to be slid over, this will clear the stream
                //

                int start = (int)dataStream.Position;
                long lengthUnProcessed = dataStream.Length - start;

               
                byte[] allData = dataStream.ToArray();
                Debug.Assert(start + lengthUnProcessed == allData.Length);
                if (start + lengthUnProcessed != allData.Length) {
                    throw new Exception(
                        String.Format("Incorrect buffer size in TryReadLine. start {0} lengthUnProcessed {1} allData.Length {2} \n allData: {3}",
                                       start, lengthUnProcessed, allData.Length, ASCIIEncoding.ASCII.GetString(allData)));
                }
                dataStream.Seek(0, SeekOrigin.Begin);
                dataStream.Write(allData, start, (int)lengthUnProcessed);
                dataStream.SetLength(lengthUnProcessed);
              
                
                return line;

            } else {

                //
                // Sad - we didn't find one full line. Restore stream position
                //
                dataStream.Position = dataStream.Length;
                return null;
            }



        }

        public static ulong FNVHash(byte[] data) {


            ulong offset = 14695981039346656037;
            ulong prime = 1099511628211;

            ulong hash = offset;
            for (int i = 0; i < data.Length; i++) {
                hash *= prime;
                hash ^= data[i];
            }

            return hash;
        }

     

        public void SendHeaderAndBody(string headerString, byte[] data) {
            //
            // Constructs a header + data + CRLF sequence and sends it
            //
            byte[] header = Encoding.ASCII.GetBytes(headerString + "\r\n");
            byte[] CRLF = new byte[] { 13, 10 };
            byte[] totalData = new byte[data.Length + header.Length + 2];
            header.CopyTo(totalData, 0);
            data.CopyTo(totalData, header.Length);
            CRLF.CopyTo(totalData, header.Length + data.Length);
            SendImpl(totalData);

        }
        public void SendValue( byte[] data, string key) {
            Debug.Assert(data != null && data.Length >0);
            SendHeaderAndBody("VALUE " + key + " " + data.Length + " " + FNVHash(data).ToString(), data);
        }

        public void SendRawData(byte[] data) {

            SendImpl(data);
        }

        public  void SendError(string err) {
            //
            // Sends an error message to the client
            //
            SendImpl( Encoding.ASCII.GetBytes("SERVER_ERROR " + err + "\r\n"));
        }

        public void SendMessage(string msg) {
            //
            // Sends an informative message to the client
            //
            SendImpl( Encoding.ASCII.GetBytes(msg + "\r\n"));
        }

        private  void SendImpl( byte[] data) {
            //
            // Blocking send of data
            //
            _socket.Send(data);
        }

    }

    //
    // Simple IAsyncResult implementation w
    // TODO: Implement the rest of the IAsyncResult model as per http://msdn.microsoft.com/msdnmag/issues/07/03/ConcurrentAffairs/
    //

    public class AsyncNetworkOperation : IAsyncResult {

        private readonly AsyncCallback _callback;
        private readonly object _state;
        private ManualResetEvent _waitHandle;
        private  Exception _exception;
        private object _result;
        private NetworkOperation _operationType;
        private long _valueSizeToRead;
        private const int NETWORK_OP_TIMEOUT = 7000; //Timeout network ops in 5 seconds

        public long ValueSizeToRead {
            get { return _valueSizeToRead; }
            set { _valueSizeToRead = value; }
        }
        

        private const int STATE_PENDING = 0;
        private const int STATE_COMPLETED = 1;

        private int _currentState = STATE_PENDING;
        private int _eventSet = 0; //1 means event has been set, 0 means it hasnt been


        public AsyncNetworkOperation(AsyncCallback callback, object state, NetworkOperation operationType) {

            _callback = callback;
            _state = state;
            _operationType = operationType;
            
        }

        public NetworkOperation OperationType {
            get { return _operationType; }
            set { _operationType = value; }
        }


        public void SetCompleted(object result, Exception ex) {
            Debug.Assert(_result == null); //Make sure no one else has set the result first
            _result = result;
            _exception = ex;

            //
            // 1. Set our internal state to completed
            // 2. if a handle has been created, unblock all waiting threads
            // 3. Call the callback
            //

            int prevState = Interlocked.Exchange(ref _currentState,STATE_COMPLETED);
            if (prevState != STATE_PENDING) {
                throw new InvalidOperationException("You can set a result only once");
            }

            if (_waitHandle != null && CallingThreadShouldSetEvent()) {
                _waitHandle.Set();
            }

            if (_callback != null) {
                _callback(this);
            }

        }

        public object EndInvoke() {
            
            //
            // Check whether we're already done. If not, block until we are and timeout if the op
            // doesnt complete soon
            //
            if (!IsCompleted) {
                if (!AsyncWaitHandle.WaitOne(NETWORK_OP_TIMEOUT, false)) {

                    _exception = new Exception("Timeout inside AsyncNetworkOperation");
                }
                AsyncWaitHandle.Close();
                _waitHandle = null;  
            }

            if (_exception != null) {
                throw _exception;
            }

            return _result;
        }

        private bool CallingThreadShouldSetEvent() {

            //
            // A little lock to ensure that the event gets set only once as otherwise
            // we'll have a race condition when multiple people set it
            //
            return (Interlocked.Exchange(ref _eventSet, 1) == 0); 
        }
        
        #region IAsyncResult Members

        public object AsyncState {
            get { return _state; }
        }

        public System.Threading.WaitHandle AsyncWaitHandle {
            //
            // Check whether someone has already created the wait event. If not, go ahead and create one and return it
            // Take to ensure that we detect the op getting completed while we're doing all this
            //
            get { 
                 if (_waitHandle == null){

                    Boolean done = IsCompleted;
                    ManualResetEvent mre = new ManualResetEvent(done);
                    if (Interlocked.CompareExchange(ref _waitHandle,  mre, null) != null) {
                       // Another thread created this object's event; dispose 
                       // the event we just created
                       mre.Close();
                    } else {
                       if (!done && IsCompleted && CallingThreadShouldSetEvent()){
                          _waitHandle.Set();
                       }
                    }
                 }
                 return _waitHandle;
              } 
        }

        public bool CompletedSynchronously {
            get { throw new NotImplementedException(); }
        }

        public bool IsCompleted {
            //
            // Returns true if the op has completed
            get { 
                return Thread.VolatileRead(ref _currentState) != STATE_PENDING;
            }
        }

        #endregion

    }
}
