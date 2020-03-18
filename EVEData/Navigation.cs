﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace SMT
{
    public enum RoutingMode
    {
        Shortest,
        Safest,
        PreferLow,
    }
}

namespace SMT.EVEData
{
    public class Navigation
    {
        private class MapNode
        {
            public bool HighSec { get; set; }
            public string Name { get; set; }
            public double Cost;
            public double MinCostToStart;
            public bool Visited;

            public double X;
            public double Y;
            public double Z;

            public double F;

            public MapNode NearestToStart;

            public List<string> Connections { get; set; }
            public List<JumpLink> JumpableSystems { get; set; }

            public string JBConnection;
        }

        public enum GateType
        {
            StarGate,
            Ansibex,
            JumpTo,
        }

        private struct JumpLink
        {
            public string System;
            public double RangeLY;
        }

        public class RoutePoint
        {
            public string SystemName { get; set; }
            public GateType GateToTake { get; set; }
            public double LY { get; set; }

            public override string ToString()
            {
                string s = SystemName;
                if (GateToTake == GateType.Ansibex)
                {
                    s += " (Ansiblex)";
                }

                if (GateToTake == GateType.JumpTo && LY > 0.0)
                {
                    s += " (Jump To, Range " + LY.ToString("0.##") + " )";
                }

                return s;
            }
        }

        public static void InitNavigation(List<System> eveSystems, List<JumpBridge> jumpBridges)
        {
            MapNodes = new Dictionary<string, MapNode>();

            // build up the nav structures
            foreach (System sys in eveSystems)
            {
                MapNode mn = new MapNode
                {
                    Name = sys.Name,
                    HighSec = sys.TrueSec > 0.45,
                    Connections = new List<string>(),
                    JumpableSystems = new List<JumpLink>(),
                    Cost = 1,
                    MinCostToStart = 0,
                    X = sys.ActualX,
                    Y = sys.ActualY,
                    Z = sys.ActualZ,
                    F = 0
                };

                foreach (string s in sys.Jumps)
                {
                    mn.Connections.Add(s);
                }

                MapNodes[mn.Name] = mn;
            }

            foreach (JumpBridge jb in jumpBridges)
            {
                MapNodes[jb.From].JBConnection = jb.To;
                MapNodes[jb.To].JBConnection = jb.From;
            }

            double MaxRange = 10 * 9460730472580800.0;

            // now create the jumpable system links
            foreach (MapNode mn in MapNodes.Values)
            {
                foreach (System sys in eveSystems)
                {
                    // cant jump into highsec systems
                    if (sys.TrueSec > 0.45)
                    {
                        continue;
                    }

                    double Distance = EveManager.Instance.GetRangeBetweenSystems(sys.Name, mn.Name);
                    if (Distance < MaxRange && Distance > 0)
                    {
                        JumpLink jl = new JumpLink();
                        jl.System = sys.Name;
                        jl.RangeLY = Distance / 9460730472580800.0;
                        mn.JumpableSystems.Add(jl);
                    }
                }
            }
        }

        public static void UpdateJumpBridges(List<JumpBridge> jumpBridges)
        {
            foreach (JumpBridge jb in jumpBridges)
            {
                if (jb.FromID != 0)
                {
                    MapNodes[jb.From].JBConnection = jb.To;
                }

                if (jb.ToID != 0)
                {
                    MapNodes[jb.To].JBConnection = jb.From;
                }
            }
        }

        public static void ClearJumpBridges()
        {
            foreach (MapNode mn in MapNodes.Values)
            {
                mn.JBConnection = null;
            }
        }

        public static List<string> GetSystemsXJumpsFrom(List<string> sysList, string start, int X)
        {
            if (MapNodes == null)
            {
                return sysList;
            }

            if (X != 0)
            {
                if (!sysList.Contains(start))
                {
                    sysList.Add(start);
                }

                MapNode mn = MapNodes[start];

                foreach (string mm in mn.Connections)
                {
                    if (!sysList.Contains(mm))
                    {
                        sysList.Add(mm);
                    }

                    List<string> connected = GetSystemsXJumpsFrom(sysList, mm, X - 1);
                    foreach (string s in connected)
                    {
                        if (!sysList.Contains(s))
                        {
                            sysList.Add(s);
                        }
                    }
                }
            }
            return sysList;
        }

        public static List<RoutePoint> Navigate(string From, string To, bool UseJumpGates, RoutingMode routingMode)
        {
            if (!(MapNodes.Keys.Contains(From)) || !(MapNodes.Keys.Contains(To)) || From == "" || To == "")

            {
                return null;
            }

            // clear the scores, values and parents from the list
            foreach (MapNode mapNode in MapNodes.Values)
            {
                mapNode.NearestToStart = null;
                mapNode.MinCostToStart = 0;
                mapNode.Visited = false;

                switch (routingMode)
                {
                    case RoutingMode.PreferLow:
                        {
                            if (mapNode.HighSec)
                                mapNode.Cost = 1000;
                        }
                        break;

                    case RoutingMode.Safest:
                        {
                            if (!mapNode.HighSec)
                                mapNode.Cost = 1000;
                        }
                        break;

                    case RoutingMode.Shortest:
                        mapNode.Cost = 1;
                        break;
                }
            }

            MapNode Start = MapNodes[From];
            MapNode End = MapNodes[To];

            List<MapNode> OpenList = new List<MapNode>();
            List<MapNode> ClosedList = new List<MapNode>();

            MapNode CurrentNode = null;

            // add the start to the open list
            OpenList.Add(Start);

            while (OpenList.Count > 0)
            {
                // get the MapNode with the lowest F score
                double lowest = OpenList.Min(mn => mn.MinCostToStart);
                CurrentNode = OpenList.First(mn => mn.MinCostToStart == lowest);

                // add the list to the closed list
                ClosedList.Add(CurrentNode);

                // remove it from the open list
                OpenList.Remove(CurrentNode);

                // walk the connections
                foreach (string connectionName in CurrentNode.Connections)
                {
                    MapNode CMN = MapNodes[connectionName];

                    if (CMN.Visited)
                        continue;

                    if (CMN.MinCostToStart == 0 || CurrentNode.MinCostToStart + CMN.Cost < CMN.MinCostToStart)
                    {
                        CMN.MinCostToStart = CurrentNode.MinCostToStart + CMN.Cost;
                        CMN.NearestToStart = CurrentNode;
                        if (!OpenList.Contains(CMN))
                        {
                            OpenList.Add(CMN);
                        }
                    }
                }

                if (UseJumpGates && CurrentNode.JBConnection != null)
                {
                    MapNode JMN = MapNodes[CurrentNode.JBConnection];
                    if (!JMN.Visited && JMN.MinCostToStart == 0 || CurrentNode.MinCostToStart + JMN.Cost < JMN.MinCostToStart)
                    {
                        JMN.MinCostToStart = CurrentNode.MinCostToStart + JMN.Cost;
                        JMN.NearestToStart = CurrentNode;
                        if (!OpenList.Contains(JMN))
                        {
                            OpenList.Add(JMN);
                        }
                    }
                }

                CurrentNode.Visited = true;
            }

            // build the path

            List<string> Route = new List<string>();

            CurrentNode = End;
            if (End.NearestToStart != null)
            {
                while (CurrentNode != null)
                {
                    Route.Add(CurrentNode.Name);
                    CurrentNode = CurrentNode.NearestToStart;
                }
                Route.Reverse();
            }

            List<RoutePoint> ActualRoute = new List<RoutePoint>();

            for (int i = 0; i < Route.Count; i++)
            {
                RoutePoint RP = new RoutePoint();
                RP.SystemName = Route[i];
                RP.GateToTake = GateType.StarGate;
                RP.LY = 0.0;

                if (i < Route.Count - 1)
                {
                    MapNode mn = MapNodes[RP.SystemName];
                    if (mn.JBConnection != null && mn.JBConnection == Route[i + 1])
                    {
                        RP.GateToTake = GateType.Ansibex;
                    }
                }
                ActualRoute.Add(RP);
            }

            return ActualRoute;
        }

        public static List<RoutePoint> NavigateCapitals(string From, string To, double MaxLY)
        {
            if (!(MapNodes.Keys.Contains(From)) || !(MapNodes.Keys.Contains(To)) || From == "" || To == "")
            {
                return null;
            }

            double ExtraJumpFactor = 5.0;

            // clear the scores, values and parents from the list
            foreach (MapNode mapNode in MapNodes.Values)
            {
                mapNode.NearestToStart = null;
                mapNode.MinCostToStart = 0;
                mapNode.Visited = false;
            }

            // reverse as we want the short jump at the start
            MapNode Start = MapNodes[To];
            MapNode End = MapNodes[From];

            List<MapNode> OpenList = new List<MapNode>();
            List<MapNode> ClosedList = new List<MapNode>();

            MapNode CurrentNode = null;

            // add the start to the open list
            OpenList.Add(Start);

            while (OpenList.Count > 0)
            {
                // get the MapNode with the lowest F score
                double lowest = OpenList.Min(mn => mn.MinCostToStart);
                CurrentNode = OpenList.First(mn => mn.MinCostToStart == lowest);

                // add the list to the closed list
                ClosedList.Add(CurrentNode);

                // remove it from the open list
                OpenList.Remove(CurrentNode);

                // walk the connections
                foreach (JumpLink connection in CurrentNode.JumpableSystems)
                {
                    if (connection.RangeLY > MaxLY)
                    {
                        continue;
                    }

                    MapNode CMN = MapNodes[connection.System];

                    if (CMN.Visited)
                        continue;

                    if (CMN.MinCostToStart == 0 || CurrentNode.MinCostToStart + connection.RangeLY + ExtraJumpFactor < CMN.MinCostToStart)
                    {
                        CMN.MinCostToStart = CurrentNode.MinCostToStart + connection.RangeLY + ExtraJumpFactor;
                        CMN.NearestToStart = CurrentNode;
                        if (!OpenList.Contains(CMN))
                        {
                            OpenList.Add(CMN);
                        }
                    }
                }

                CurrentNode.Visited = true;
            }

            // build the path

            List<string> Route = new List<string>();

            CurrentNode = End;
            if (End.NearestToStart != null)
            {
                while (CurrentNode != null)
                {
                    Route.Add(CurrentNode.Name);
                    CurrentNode = CurrentNode.NearestToStart;
                }
            }

            List<RoutePoint> ActualRoute = new List<RoutePoint>();

            for (int i = 0; i < Route.Count; i++)
            {
                RoutePoint RP = new RoutePoint();
                RP.SystemName = Route[i];
                RP.GateToTake = GateType.StarGate;
                RP.LY = 0.0;

                if (i > 0)
                {
                    RP.GateToTake = GateType.JumpTo;
                    RP.LY = EveManager.Instance.GetRangeBetweenSystems(Route[i], Route[i - 1]) / 9460730472580800.0;
                }
                ActualRoute.Add(RP);
            }

            return ActualRoute;
        }

        private static Dictionary<string, MapNode> MapNodes { get; set; }
    }
}