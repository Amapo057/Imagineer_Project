using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ModernDrawPlayVerification
{
    const string Key="KJH.ModernDrawPlayVerification";
    static double started;
    static int frames,stage;
    static GameManager game;
    static DemoTurnController controller;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static ModernDrawPlayVerification()
    {
        EditorApplication.playModeStateChanged+=state=>
        {
            if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false))
            {
                started=EditorApplication.timeSinceStartup;frames=Time.frameCount;stage=0;EditorApplication.update+=Tick;
            }
        };
    }
    public static void Start()
    {
        EditorSceneManager.OpenScene("Assets/0. Scene/KJH_CardTest.unity");
        foreach(var init in UnityEngine.Object.FindObjectsByType<StartInit>(FindObjectsSortMode.None))init.enabled=false;
        foreach(var marker in UnityEngine.Object.FindObjectsByType<TrackingMarker>(FindObjectsSortMode.None))marker.enabled=false;
        foreach(var move in UnityEngine.Object.FindObjectsByType<CardMove>(FindObjectsSortMode.None))move.enabled=false;
        foreach(var draw in UnityEngine.Object.FindObjectsByType<DrawManager>(FindObjectsSortMode.None))
        {
            var field=typeof(DrawManager).GetField("cardPrefabs",Flags);
            var template=UnityEngine.Object.Instantiate((GameObject)field.GetValue(draw));
            template.transform.position=Vector3.down*1000;
            foreach(var move in template.GetComponentsInChildren<CardMove>(true))move.enabled=false;
            foreach(var marker in template.GetComponentsInChildren<TrackingMarker>(true))marker.enabled=false;
            field.SetValue(draw,template);
        }
        // This desktop test has no XR device. Change only the in-memory test session.
        var xr=UnityEngine.XR.Management.XRGeneralSettings.Instance;
        if(xr!=null)
        {
            xr.InitManagerOnStart=false;
            if(xr.Manager!=null){xr.Manager.automaticLoading=false;xr.Manager.automaticRunning=false;}
        }
        SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void CheckHands()
    {
        foreach(string field in new[]{"myDrawManager","enemyDrawManager"})
        {
            var draw=(DrawManager)typeof(DemoTurnController).GetField(field,Flags).GetValue(controller);
            Check(draw!=null,"Missing DrawManager "+field);
            var visuals=(IList)typeof(DrawManager).GetField("handCards",Flags).GetValue(draw);
            var board=game.Board.GetBoard(draw.Side);
            Check(visuals.Count==board.hand.Count,$"Hand visuals mismatch {field}: {visuals.Count} vs {board.hand.Count}");
        }
    }
    static void Tick()
    {
        try
        {
            if(!EditorApplication.isPlaying)return;
            foreach(var init in UnityEngine.Object.FindObjectsByType<StartInit>(FindObjectsSortMode.None))init.enabled=false;
            foreach(var marker in UnityEngine.Object.FindObjectsByType<TrackingMarker>(FindObjectsSortMode.None))marker.enabled=false;
            foreach(var move in UnityEngine.Object.FindObjectsByType<CardMove>(FindObjectsSortMode.None))move.enabled=false;
            double elapsed=EditorApplication.timeSinceStartup-started;
            if(elapsed>30)throw new Exception("Play test timeout");
            if(stage==0 && elapsed>2)
            {
                Check(Time.frameCount>frames+5,"Play mode is not advancing");
                controller=UnityEngine.Object.FindFirstObjectByType<DemoTurnController>();
                Check(controller!=null,"Missing demo controller");
                game=(GameManager)typeof(DemoTurnController).GetField("gameManager",Flags).GetValue(controller);
                Check(game.Board.me.hand.Count+game.Board.opponent.hand.Count==13,"Expected opening 6+6 and first auto draw 1");
                CheckHands();
                int before=game.Board.me.hand.Count+game.Board.opponent.hand.Count;
                controller.OnDrawButtonClicked();controller.OnDrawButtonClicked();
                Check(game.Board.me.hand.Count+game.Board.opponent.hand.Count==before,"Draw button granted extra cards");
                controller.OnEndTurnButtonClicked();stage=1;started=EditorApplication.timeSinceStartup;
            }
            else if(stage==1 && elapsed>1)
            {
                Check(game.Board.me.hand.Count==7&&game.Board.opponent.hand.Count==7,"Second player auto draw failed");
                CheckHands();
                game.DrawCards(PlayerSide.Me,10);game.DrawCards(PlayerSide.Opponent,10);
                Check(game.Board.me.hand.Count==12&&game.Board.opponent.hand.Count==12,"Draw to hand cap 12 failed");
                CheckHands();
                Debug.Log("MODERN_DRAW_PLAY_SUCCESS: frames advancing; opening6; auto1; hand cap12 with 24 matching visuals; no duplicate button draws");
                SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(0);
            }
        }
        catch(Exception e)
        {
            Debug.LogException(e);SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(1);
        }
    }
}
