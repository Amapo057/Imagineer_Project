using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using KJH.Balancing;
using UnityEditor;
using UnityEngine;

public static class LocalNetworkVerification
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed class Peer
    {
        public GameNetworkManager net;
        public GameManager game;
        public SecondPlayerCoin coin;
        public int starts;
        public readonly List<string> events=new List<string>();
        public readonly List<int> probes=new List<int>();
        public void Reset(){game=new GameManager(false);game.StartGame(PlayerSide.Me);coin=new SecondPlayerCoin(game);}
        public void Bind(CardDatabase cards)
        {
            Reset();
            net.OnGameStartReceived+=side=>{game=new GameManager(false);game.StartGame(side);coin=new SecondPlayerCoin(game);starts++;events.Add("start");};
            net.OnEndTurnReceived+=()=>game.EndTurn();
            net.OnCoinUsedReceived+=()=>coin.TryUse(PlayerSide.Opponent);
            net.OnRematchReceived+=()=>{Reset();events.Add("reset");};
            net.OnCardPlayedReceived+=(id,lane)=>
            {
                if(id>=1000){probes.Add(id);return;}
                Place(this,cards.GetCardById(id),PlayerSide.Opponent,lane);
            };
        }
    }
    static void Set(GameNetworkManager net,string field,object value)=>typeof(GameNetworkManager).GetField(field,Private).SetValue(net,value);
    static object Get(GameNetworkManager net,string field)=>typeof(GameNetworkManager).GetField(field,Private).GetValue(net);
    static void Call(GameNetworkManager net,string name,params object[] args)=>typeof(GameNetworkManager).GetMethod(name,Private).Invoke(net,args);
    static void Check(bool value,string message){if(!value)throw new Exception(message);}

    public static void Run()
    {
        Peer host=null,client=null;
        try
        {
            var cards=AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/2. Data/Card/CardDatabase.asset");
            var staticInstance=typeof(GameNetworkManager).GetField("instance",BindingFlags.Static|BindingFlags.NonPublic);
            host=new Peer{net=new GameObject("TCP verification host").AddComponent<GameNetworkManager>()};
            staticInstance.SetValue(null,null);
            client=new Peer{net=new GameObject("TCP verification client").AddComponent<GameNetworkManager>()};
            host.Bind(cards);client.Bind(cards);
            Set(host.net,"isHost",true);Set(client.net,"isHost",false);
            var hostThread=new Thread(()=>Call(host.net,"RunAsHost")){IsBackground=true};
            Set(host.net,"sessionThread",hostThread);hostThread.Start();
            Wait(host,client,()=>Get(host.net,"listener") is TcpListener l && l.Server.IsBound,"host listener",5);
            var clientThread=new Thread(()=>Call(client.net,"RunAsClient",IPAddress.Loopback)){IsBackground=true};
            Set(client.net,"sessionThread",clientThread);clientThread.Start();
            Wait(host,client,()=>host.net.Connected&&client.net.Connected&&host.starts==1&&client.starts==1,"TCP handshake",10);
            Equal(host,client);Debug.Log("LOCAL_TCP_HANDSHAKE_PASS");

            var first=host.game.State.CurrentPlayer==PlayerSide.Me?host:client;
            var second=ReferenceEquals(first,host)?client:host;
            Place(first,cards.GetCardById(1),PlayerSide.Me,0);first.net.SendCardPlayed(1,0);
            Wait(host,client,()=>second.game.Board.opponent.lanes[0]!=null,"card placement",5);
            Equal(host,client);
            Check(first.net.TrySendEndTurn(PlayerSide.Me,first.game.State.CurrentPlayer),"First player end turn rejected");first.game.EndTurn();
            Wait(host,client,()=>second.game.State.CurrentPlayer==PlayerSide.Me,"end turn",5);
            Check(second.coin.TryUse(PlayerSide.Me),"Second player cannot use coin");second.net.SendCoinUsed();
            Wait(host,client,()=>first.game.Board.opponent.currentCost==3,"coin synchronization",5);
            Equal(host,client);
            Place(second,cards.GetCardById(5),PlayerSide.Me,1);second.net.SendCardPlayed(5,1);
            Wait(host,client,()=>first.game.Board.opponent.lanes[1]!=null,"coin-funded card",5);
            Equal(host,client);
            Check(second.net.TrySendEndTurn(PlayerSide.Me,second.game.State.CurrentPlayer),"Second player end turn rejected");second.game.EndTurn();
            Wait(host,client,()=>first.game.State.CurrentPlayer==PlayerSide.Me,"combat synchronization",5);
            Equal(host,client);Debug.Log("LOCAL_TCP_GAMEPLAY_PASS: cards, mana, coin, turn, combat, face hits");

            var stream=(NetworkStream)Get(host.net,"stream");
            string payload=string.Concat(Enumerable.Range(1000,300).Select(i=>$"{NetMsg.CardPlayed}|{i}|0\n"));
            byte[] bytes=Encoding.UTF8.GetBytes(payload);
            int[] sizes={1,2,3,5,17,4096};int offset=0,index=0;
            while(offset<bytes.Length){int count=Math.Min(sizes[index++%sizes.Length],bytes.Length-offset);stream.Write(bytes,offset,count);offset+=count;}
            Wait(host,client,()=>client.probes.Count==300,"fragmented/batched TCP messages",5);
            Check(client.probes.SequenceEqual(Enumerable.Range(1000,300)),"TCP message loss or order mismatch");
            Debug.Log("LOCAL_TCP_FRAMING_PASS: 300 split/coalesced messages preserved in order");

            for(int i=0;i<12;i++)
            {
                int hs=host.starts,cs=client.starts;
                int eventIndex=host.events.Count;
                var sender=i%2==0?client:host;
                sender.net.SendRematch();sender.Reset();
                Wait(host,client,()=>host.starts>hs&&client.starts>cs,"rematch "+i,5);
                if(ReferenceEquals(sender,client))Check(host.events.Skip(eventIndex).SequenceEqual(new[]{"reset","start"}),"Rematch callback ordering incorrect");
                Equal(host,client);
                Check(host.coin.IsAvailable(PlayerSide.Me)!=client.coin.IsAvailable(PlayerSide.Me),"Coin ownership mismatch after rematch");
            }
            Debug.Log("LOCAL_TCP_REMATCH_PASS: 12 alternating client/host rematches synchronized");
            Debug.Log("LOCAL_NETWORK_VERIFICATION_SUCCESS");
        }
        finally
        {
            foreach(var peer in new[]{host,client})if(peer!=null)Set(peer.net,"running",false);
            foreach(var peer in new[]{host,client})if(peer?.net!=null)UnityEngine.Object.DestroyImmediate(peer.net.gameObject);
        }
    }

    static void Place(Peer peer,CardData data,PlayerSide side,int lane)
    {
        var board=peer.game.Board.GetBoard(side);
        Check(peer.game.State.CurrentPlayer==side&&board.currentCost>=data.cost&&board.lanes[lane]==null,"Invalid network placement");
        board.lanes[lane]=new CardInstance(data,side);board.currentCost-=data.cost;
    }

    static void Wait(Peer a,Peer b,Func<bool> done,string operation,int seconds)
    {
        var deadline=DateTime.UtcNow.AddSeconds(seconds);
        do
        {
            Call(a.net,"Update");Call(b.net,"Update");
            if(done())return;
            Thread.Sleep(5);
        }while(DateTime.UtcNow<deadline);
        throw new Exception("Timed out: "+operation);
    }

    static void Equal(Peer a,Peer b)
    {
        Check(a.game.State.CurrentPlayer!=b.game.State.CurrentPlayer,"Turn ownership desynchronized");
        for(int side=0;side<2;side++)
        {
            var left=a.game.Board.GetBoard((PlayerSide)side);var right=b.game.Board.GetBoard((PlayerSide)(1-side));
            Check(left.isSecondPlayer==right.isSecondPlayer&&left.currentCost==right.currentCost&&left.faceHitCount==right.faceHitCount,"Board resource / first-player desynchronized");
            for(int lane=0;lane<4;lane++)
            {
                var x=left.lanes[lane];var y=right.lanes[lane];
                Check((x==null)==(y==null),"Lane occupancy desynchronized");
                if(x!=null)Check(x.data.cardId==y.data.cardId&&x.currentHealth==y.currentHealth&&x.currentAttack==y.currentAttack,"Card stats desynchronized");
            }
        }
    }
}
