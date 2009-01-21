using System;
using System.Configuration.Install;
using System.ComponentModel;
using System.ServiceProcess;

namespace CachemanServer {
    [RunInstaller(true)]
    public class SvcInstaller:Installer {

        public SvcInstaller() {

            this.Committed += new InstallEventHandler(SvcInstaller_Committed);

            ServiceInstaller svcInstall = new ServiceInstaller();
            svcInstall.ServiceName = Global.SERVICE_NAME;
            svcInstall.StartType = ServiceStartMode.Automatic;
            
            ServiceProcessInstaller spInstall = new ServiceProcessInstaller();
            spInstall.Account = ServiceAccount.NetworkService;
            spInstall.Username = null;
            spInstall.Password = null;

            this.Installers.AddRange(new Installer[] { svcInstall, spInstall });
        }

        void SvcInstaller_Committed(object sender, InstallEventArgs e) {
            ServiceController svc = new ServiceController(Global.SERVICE_NAME);
            svc.Start();
        }

        public override void Install(System.Collections.IDictionary stateSaver) {
            PerfCounters.InstallPerfCounters();
            base.Install(stateSaver);
        }

        public override void Uninstall(System.Collections.IDictionary savedState) {
            PerfCounters.RemovePerfCounters();
            base.Uninstall(savedState);
        }

      
    }
}
