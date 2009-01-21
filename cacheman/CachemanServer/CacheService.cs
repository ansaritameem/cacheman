using System.ServiceProcess;
using CachemanCommon;
using System.Threading;
using System.Net;

namespace CachemanServer {
   public class CacheService:ServiceBase {

        private CacheServer _mainServer;
        private Thread _listenThread;

        public CacheService() {
            this.ServiceName = Global.SERVICE_NAME;
            this.AutoLog = true;
            this.CanStop = true;
            this.CanPauseAndContinue = false;
        }
        protected override void OnStart(string[] args) {


            if (args != null && args.Length > 0) {
                Utils.ParseAndExecCmdLine(args);
            }
           
            
            if (Global.IPV4ADDR == IPAddress.Any) {
                Log.LogError("Error! Couldn't find a 10.*.*.* or a 192.*.*.* or a 157.*.*.* interface to bind to for the service. If you want to bind" +
                    " to an address not in that range, pass it explicity as a parameter");
                return;                          

            }
            _mainServer = new CacheServer(Global.PORT, Global.IPV4ADDR);
            _listenThread = new Thread(new ThreadStart(_mainServer.StartListening));
            _listenThread.Start();
            
        }

        protected override void OnStop() {

            _mainServer.StopListening();
            _listenThread.Abort();
            _listenThread.Join();
            base.OnStop();
        }
       
    }
}
