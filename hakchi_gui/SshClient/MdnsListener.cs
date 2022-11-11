﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Tmds.MDns;

namespace com.clusterrr.ssh
{
    public class MdnsListener : IListener
    {
        public IList<Device> Available
        {
            get; private set;
        }

        ServiceBrowser serviceBrowser;
        string serviceName;
        string serviceType;

        public MdnsListener(string name, string type)
        {
            Available = new List<Device>();
            serviceName = name;
            serviceType = type;

            // enable service browser
            serviceBrowser = new ServiceBrowser();
            serviceBrowser.ServiceAdded += onServiceAdded;
            serviceBrowser.ServiceChanged += onServiceChanged;
            serviceBrowser.ServiceRemoved += onServiceRemoved;
            serviceBrowser.StartBrowse(type);
        }

        public void Cycle()
        {
            // no-op
        }

        public void Dispose()
        {
            serviceBrowser.StopBrowse();
            Available.Clear();
        }

        private void debugAnnouncement(string header, ServiceAnnouncement a)
        {
            Trace.WriteLine(header);
            Trace.Indent();
            Trace.WriteLine("Instance: " + a.Instance);
            Trace.WriteLine("Type: " + a.Type);
            Trace.WriteLine("IP: " + string.Join(", ", a.Addresses));
            Trace.WriteLine("Port: " + a.Port);
            Trace.WriteLine("Txt: " + string.Join(", ", a.Txt));
            Trace.Unindent();
        }

        private void onServiceAdded(object sender, ServiceAnnouncementEventArgs e)
        {
            // ignore other services
            if (e.Announcement.Instance != this.serviceName)
            {
                return;
            }

            // debug
            debugAnnouncement("Service added:", e.Announcement);

            // create entry
            var dev = new Device()
            {
                Addresses = e.Announcement.Addresses,
                Port = e.Announcement.Port,
            };

            // build device info
            foreach (var txt in e.Announcement.Txt)
            {
                var tokens = txt.Split('=');
                if (tokens.Length == 2)
                {
                    switch (tokens[0])
                    {
                        case "hwid":
                            dev.UniqueID = tokens[1].Replace(" ", "").ToUpper();
                            break;

                        case "type":
                            dev.ConsoleType = tokens[1];
                            break;

                        case "region":
                            dev.ConsoleRegion = tokens[1];
                            break;
                    }
                }
            }

            // check to avoid adding duplicate devices
            foreach (var a in Available)
            {
                if (dev.Addresses.SequenceEqual(e.Announcement.Addresses))
                {
                    Trace.WriteLine("Duplicate announce for addresses: " + string.Join(", ", e.Announcement.Addresses));
                    return;
                }
                if (dev.UniqueID == a.UniqueID)
                {
                    Trace.WriteLine("Duplicate announce for same device: " + a.UniqueID);
                    return;
                }
            }

            Available.Add(dev);
        }

        private void onServiceChanged(object sender, ServiceAnnouncementEventArgs e)
        {
            // silently ignore other services
            if (e.Announcement.Instance != this.serviceName)
            {
                return;
            }
            debugAnnouncement("A service changed:", e.Announcement);
        }

        private void onServiceRemoved(object sender, ServiceAnnouncementEventArgs e)
        {
            // silently ignore other services
            if (e.Announcement.Instance != this.serviceName)
            {
                return;
            }
            debugAnnouncement("A service was removed:", e.Announcement);

            foreach (var a in Available)
            {
                if (a.Addresses.SequenceEqual(e.Announcement.Addresses))
                {
                    Available.Remove(a);
                    return;
                }
            }
            Trace.WriteLine("Service had not been detected before. Hmmm...");
        }

    }
}
