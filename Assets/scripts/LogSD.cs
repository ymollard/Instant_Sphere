using System;
using System.IO;
using UnityEngine;

public sealed class LogSD
{
    string mFileDateStr;

    // RT = Real Time; HQ = High Quality
    public enum enum_state { RT, HQ };
    enum_state mState;

    public LogSD()
    {
        mState = enum_state.RT;
    }

    private string NewDate()
    {
        return DateTime.Now.ToString("dd-MM-yyyy_HH.mm.ss");
    }

    private void WriteFile(string toPrint)
    {
        try
        {
            StreamWriter streamWriter = new StreamWriter(Application.persistentDataPath + "/" + mFileDateStr + ".log", true);
            Debug.Log(Application.persistentDataPath + "/" + mFileDateStr + ".log");
            streamWriter.WriteLine(toPrint);
            streamWriter.Close();
        }
        catch (Exception ex)
        {
            Debug.Log("Error writing log file : " + ex.Message);
        }
    }

    private void NewFile()
    {
        mFileDateStr = NewDate();
    }

    public enum_state State()
    {
        return mState;
    }

    public void ChangeToRT()
    {
        mState = enum_state.RT;
    }

    public void ChangeToHQ()
    {
        mState = enum_state.HQ;
    }

    public void WriteTimeout(ScreensController.ScreensStates currentState)
    {
        WriteFile("\t{\"event\": \"timeout\", \"time\": \"" + NewDate() + "\", \"state\": \"" + currentState + "\"}");
        WriteFile("]");
    }

    public void WriteStart()
    {
        NewFile();
        mState = enum_state.RT;
        WriteFile("[");
        WriteFile("\t{\"event\": \"start\", \"time\": \"" + NewDate() + "\"},");
    }

    public void WriteCapture()
    {
        WriteFile("\n\t{\"event\": \"capture\", \"time\": \"" + NewDate() + "\"},");
    }

    public void WriteVisualizeAbandon()
    {
        WriteFile("\n\t{\"event\": \"visualize\", \"time\": \"" + NewDate() + "\", \"choice\": \"abandon\"}");
        WriteFile("]");
    }

    public void WriteVisualizeRestart()
    {
        WriteFile("\n\t{\"event\": \"visualize\", \"time\": \"" + NewDate() + "\", \"choice\": \"restart\"},");
    }

    public void WriteVisualizeShare()
    {
        WriteFile("\n\t{\"event\": \"visualize\", \"time\": \"" + NewDate() + "\", \"choice\": \"share\"},");
    }

    public void WriteShareFacebook()
    {
        WriteFile("\n\t{\"event\": \"share\", \"time\": \"" + NewDate() + "\", \"choice\": \"facebook\"},");
    }

    public void WriteShareAbandon()
    {
        WriteFile("\n\t{\"event\": \"share\", \"time\": \"" + NewDate() + "\", \"choice\": \"abandon\"}");
        WriteFile("]");
    }

    public void WriteNavigateRT()
    {
        WriteFile("\t{\"event\": \"navigate_RT\", \"time\": \"" + NewDate() + "\"},");
    }

    public void WriteNavigateHD()
    {
        WriteFile("\t{\"event\": \"navigate_HD\", \"time\": \"" + NewDate() + "\"},");
    }
}
