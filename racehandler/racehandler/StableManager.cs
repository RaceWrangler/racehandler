using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;


/*
 * The stable manager keeps track of the various
 * spies present on the network.
 * 
 * It does this using netmq to detect their
 * presence on the network and it then adds them
 * to a list of spies.
 * 
 * This list is used to idenfity where the directories
 * that need to be monitored for new dead drops are
 * located on the network.
 */

namespace racehandler
{
    class StableManager
    {
        // Actor Protocol
        public const string PublishCommand = "P";
        public const string GetHostAddressCommand = "GetHostAddress";
        public const string AddedNodeCommand = "AddedNode";
        public const string RemovedNodeCommand = "RemovedNode";

        private readonly TimeSpan burnNoticeTimeout = TimeSpan.FromSeconds(10);

        public class Spy
        {
            public Spy(string name, int port)
            {
                Name = name;
                Port = port;
                Address = $"tcp://{name}:{port}";
                HostName = Dns.GetHostEntry(name).HostName;
            }

            public string Name { get; }
            public int Port { get; }
            public string Address { get; }
            public object HostName { get; private set; }

            protected bool Equals(Spy other)
            {
                return string.Equals(Name, other.Name) && Port == other.Port;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((Spy)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Name?.GetHashCode() ?? 0) * 397) ^ Port;
                }
            }

            public override string ToString()
            {
                return Address;
            }
        }

        private readonly int m_broadcastPort;
        private readonly NetMQActor m_actor;

        private PublisherSocket m_publisher;
        private SubscriberSocket m_subscriber;
        private NetMQBeacon m_beacon;
        private NetMQPoller m_poller;
        private PairSocket m_shim;
        private readonly Dictionary<Spy, DateTime> m_nodes; // value is the last time we "saw" this node
        private int m_randomPort;

        private StableManager(int broadcastPort)
        {
            m_nodes = new Dictionary<Spy, DateTime>();
            m_broadcastPort = broadcastPort;
            m_actor = NetMQActor.Create(RunActor);
        }

        /// <summary>
        /// Creates a new message bus actor. All communication with the bus is
        /// through the returned <see cref="NetMQActor"/>.
        /// </summary>
        public static NetMQActor Create(int broadcastPort)
        {
            StableManager node = new StableManager(broadcastPort);
            return node.m_actor;
        }

        private void RunActor(PairSocket shim)
        {
            // save the shim to the class to use later
            m_shim = shim;

            // create all subscriber, publisher and beacon
            using (m_subscriber = new SubscriberSocket())
            using (m_publisher = new PublisherSocket())
            using (m_beacon = new NetMQBeacon())
            {
                // listen to actor commands
                m_shim.ReceiveReady += OnShimReady;

                // subscribe to all messages
                m_subscriber.Subscribe("");

                // we bind to a random port, we will later publish this port
                // using the beacon
                m_randomPort = m_subscriber.BindRandomPort("tcp://*");
                Console.WriteLine("Bus subscriber is bound to {0}", m_subscriber.Options.LastEndpoint);

                // listen to incoming messages from other publishers, forward them to the shim
                m_subscriber.ReceiveReady += OnSubscriberReady;

                // configure the beacon to listen on the broadcast port
                Console.WriteLine("Beacon is being configured to UDP port {0}", m_broadcastPort);
                m_beacon.Configure(m_broadcastPort);

                // publishing the random port to all other nodes
                Console.WriteLine("Beacon is publishing the Bus subscriber port {0}", m_randomPort);
                m_beacon.Publish(m_randomPort.ToString(), TimeSpan.FromSeconds(1));

                // Subscribe to all beacon on the port
                Console.WriteLine("Beacon is subscribing to all beacons on UDP port {0}", m_broadcastPort);
                m_beacon.Subscribe("");

                // listen to incoming beacons
                m_beacon.ReceiveReady += OnBeaconReady;

                // Create a timer to clear dead nodes
                NetMQTimer timer = new NetMQTimer(TimeSpan.FromSeconds(1));
                timer.Elapsed += BurnDeadSpies;

                // Create and configure the poller with all sockets and the timer
                m_poller = new NetMQPoller { m_shim, m_subscriber, m_beacon, timer };

                // signal the actor that we finished with configuration and
                // ready to work
                m_shim.SignalOK();

                // polling until cancelled
                m_poller.Run();
            }
        }

        private void OnShimReady(object sender, NetMQSocketEventArgs e)
        {
            // new actor command
            string command = m_shim.ReceiveFrameString();

            // check if we received end shim command
            if (command == NetMQActor.EndShimMessage)
            {
                // we cancel the socket which dispose and exist the shim
                m_poller.Stop();
            }
            else if (command == PublishCommand)
            {
                // it is a publish command
                // we just forward everything to the publisher until end of message
                NetMQMessage message = m_shim.ReceiveMultipartMessage();
                m_publisher.SendMultipartMessage(message);
            }
            else if (command == GetHostAddressCommand)
            {
                var address = m_beacon.BoundTo + ":" + m_randomPort;
                m_shim.SendFrame(address);
            }
        }

        private void OnSubscriberReady(object sender, NetMQSocketEventArgs e)
        {
            // we got a new message from the bus
            // let's forward everything to the shim
            NetMQMessage message = m_subscriber.ReceiveMultipartMessage();
            m_shim.SendMultipartMessage(message);
        }

        private void OnBeaconReady(object sender, NetMQBeaconEventArgs e)
        {
            // we got another beacon
            // let's check if we already know about the beacon
            var message = m_beacon.Receive();
            int port;
            int.TryParse(message.String, out port);

            Spy node = new Spy(message.PeerHost, port);

            // check if node already exist
            if (!m_nodes.ContainsKey(node))
            {
                // we have a new node, let's add it and connect to subscriber
                m_nodes.Add(node, DateTime.Now);
                m_publisher.Connect(node.Address);
                m_shim.SendMoreFrame(AddedNodeCommand).SendFrame(node.Address);
            }
            else
            {
                //Console.WriteLine("Node {0} is not a new beacon.", node);
                m_nodes[node] = DateTime.Now;
            }
        }

        private void BurnDeadSpies(object sender, NetMQTimerEventArgs e)
        {
            // create an array with the dead nodes
            var deadNodes = m_nodes.
                Where(n => DateTime.Now > n.Value + burnNoticeTimeout)
                .Select(n => n.Key).ToArray();

            // remove all the dead nodes from the nodes list and disconnect from the publisher
            foreach (var node in deadNodes)
            {
                m_nodes.Remove(node);
                m_publisher.Disconnect(node.Address);
                m_shim.SendMoreFrame(RemovedNodeCommand).SendFrame(node.Address);
            }
        }
    }
}
