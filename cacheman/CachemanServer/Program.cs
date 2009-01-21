using System;
using System.Text;
using CachemanCommon;


namespace CachemanServer
{
    class Program
    {
         static void Main(string[] args){
           
           
           Log.LogMessage("Cacheman Server " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + "\n" +
               "Feedback and bugs - sriramk@microsoft.com\n" +
                "Run cachemanserver /? to get a list of command line options\n" +
                "NOTE:If you're not using /interactive, this will try and launch the Cacheman service. Use /interactive to make this process host Cacheman instead\n"               
               );
           Utils.ParseAndExecCmdLine(args);

            if (Global.IS_INTERACTIVE) {
                Log.UseEventLog = false;
               (new CacheServer(Global.PORT, Global.IPV4ADDR)).StartListening();
           } else {
                Log.UseEventLog = true;
               System.ServiceProcess.ServiceBase.Run(new CacheService());
           }
           
        }

        

        




       
    }
}
