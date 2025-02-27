﻿using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using LitJson;
using System;
using System.Text;

/**
 * This class is intended to handle an 360 degrees camera that provide OSC API 2
 * It uses a FSM to follow the camera state
 **/
public sealed class OSCController : MonoBehaviour
{
    HttpRequest mHTTP = new HttpRequest();
    Queue<MethodInfo> mExecutionQueue = new Queue<MethodInfo>();    //queue for methods that should be executed
    osc_controller_data mInternalData;                              //structure holding ID's
    Action mCallBack = null;                                        //callback to signal that photo download is finish

    byte[] mBuffer;
    enum OSCStates { INIT, DISCONNECTED, IDLE, LIVE_PREVIEW, TAKE_PHOTO, DOWNLOAD_PHOTO, DELETE_PHOTO, ERROR, SEND_CAMERA_INFO };
    OSCStates mCurrentState;

    /* Actions associated with the method name */
    enum OSCActions { START_SESSION = 0, UPGRADE_API, SET_OPTIONS, TAKE_PICTURE, DOWNLOAD, PROGRESS_STATUS, DELETE, LIVE_PREVIEW, CAMERA_STATE };
    string[] mActionsMethodName = { "AskStartSession", "AskUpgradeAPI", "AskSetOptions", "AskTakePicture", "AskDownloadPhoto", "AskProgressStatus", "AskDeletePhoto", "AskStartLivePreview", "AskCameraState" };


    /**
     * Use this for initialization
     **/
    private void Start()
    {
        Init();
    }

    private void Init()
    {
        mInternalData.fileURL = "";
        mInternalData.currentOperationId = "";
        mInternalData.sessionId = "";
        mInternalData.isBusy = false;
        mInternalData.remainingConnectionTry = 3;
        mCurrentState = OSCStates.INIT;
        EnqueueAction(OSCActions.CAMERA_STATE);
    }

    /**
     * Call this method to start capturing and download a photo
     * The system should be in IDLE state otherwise throw an exception
     * callback is a void(void) function that will be called when the photo downloading is finish
     **/
    public void StartCapture(Action callback)
    {
        if (mCurrentState != OSCStates.IDLE)
            throw new InvalidOperationException("OSC controller wasn't in IDLE state when trying to take a picture.");
        EnqueueAction(OSCActions.TAKE_PICTURE);
        mCallBack = callback;
    }

    /**
     * Call this method to start live preview acquisition
     * The system should be in IDLE state otherwise throw an exception
     **/
    public void StartLivePreview()
    {
        if (mCurrentState != OSCStates.IDLE)
            throw new InvalidOperationException("OSC controller wasn't in IDLE state when trying to start live preview.");
        EnqueueAction(OSCActions.LIVE_PREVIEW);
    }

    /**
     * Stop a live preview acquisition going back to IDLE state and closing streaming connection
     **/
    public void StopLivePreview()
    {
        mInternalData.isBusy = false;
        mCurrentState = OSCStates.IDLE;
        mHTTP.CloseStreaming();
    }

    /**
     * Get the last downloaded photo as byte buffer
     * or null if no new image is available
     **/
    public byte[] GetLatestData()
    {
        byte[] ret = mBuffer;
        mBuffer = null;
        return ret;
    }

    /**
     * Return true if camera works, false if the system can't communicate with the camera
     **/
    public bool IsCameraOK()
    {
        return mCurrentState != OSCStates.ERROR;
    }

    /**
     * Use this to reboot the OSC controller when necessary (ie after disconnecting / reconnecting to the camera)
     **/
    public void RebootController()
    {
        ClearQueue();
        Init();
    }

    /**
     * Enqueue the specified action
     **/
    private void EnqueueAction(OSCActions action)
    {
        string methodName = mActionsMethodName[(int)action];
        MethodInfo mi = GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        mExecutionQueue.Enqueue(mi);
    }

    /**
     * Remove all actions waiting in the queue
     **/
    private void ClearQueue()
    {
        mExecutionQueue.Clear();
    }

    /**
     * Issues enqueued commands and gets response
     **/
    private void Update()
    {
        //Dequeue and invoke a new method if we are done with the previous request
        if (!mInternalData.isBusy && mExecutionQueue.Count > 0)
        {
            mExecutionQueue.Dequeue().Invoke(this, null);
            mInternalData.isBusy = true;
        }
        else if (mInternalData.isBusy && mHTTP.IsTerminated())  //else if the request is terminated and successful handle it in the FSM
        {
            string s = mHTTP.GetHTTPResponse();
            Debug.Log("Request answer: " + s);
            if (mHTTP.IsSuccessful())
            {
                mInternalData.remainingConnectionTry = 3;
                ResponseHandler(s);
            }
            else
                HandleError(s);
            mInternalData.isBusy = false;
        }
        else if (mCurrentState == OSCStates.LIVE_PREVIEW)    //else if we are streaming check for a new image or try restarting live preview
        {
            if(mHTTP.mStreamRequest.IsStreamOnError())
            {
                if (mInternalData.remainingConnectionTry > 0)
                {
                    int tmp = mInternalData.remainingConnectionTry;
                    StopLivePreview();
                    RebootController();
                    EnqueueAction(OSCActions.LIVE_PREVIEW);
                    mInternalData.remainingConnectionTry = --tmp;
                }
                else
                {
                    ClearQueue();
                    mCurrentState = OSCStates.ERROR;
                }
            }
            else
            {
                ResponseHandler(null);
            }
        }
    }

    /**
     * Handle error from the HTTP part
     **/
    private void HandleError(string err)
    {
        if (mInternalData.remainingConnectionTry > 0)
        {
            int tmp = mInternalData.remainingConnectionTry;
            Debug.Log("HTTP error: " + err);        //Log error message
            RebootController();
            mInternalData.remainingConnectionTry = --tmp;
        }
        else    //3 try, 3 fails, application goes to error state
        {
            ClearQueue();
            mCurrentState = OSCStates.ERROR;
        }
    }

    /**
     * Handle normal response from the HTTP part and update the FSM accordingly
     **/
    private void ResponseHandler(string result)
    {
        JsonData jdata;
        try
        {
            jdata = HttpRequest.JSONStringToDictionary(result);
        }
        catch
        {
            jdata = null;
        }

        switch (mCurrentState)
        {
            case OSCStates.INIT:
                ManageInit(jdata);
                break;
            case OSCStates.DISCONNECTED:
                ManageDisconnected(jdata);
                break;
            case OSCStates.IDLE:
                ManageIdle(jdata);
                break;
            case OSCStates.LIVE_PREVIEW:
                ManageLivePreview();
                break;
            case OSCStates.TAKE_PHOTO:
                ManageTakePhoto(jdata);
                break;
            case OSCStates.DOWNLOAD_PHOTO:
                ManageDownload(jdata);
                break;
            case OSCStates.DELETE_PHOTO:
                ManageDelete(jdata);
                break;
        }
    }

    /**
     * Passes the camera in API version 2 if it is in version 1
     * then sets options
     **/
    void ManageInit(JsonData jdata)
    {
        Debug.Log(jdata.ToString());
        if(jdata["state"]["_apiVersion"].ToString() == "1")
        {
            EnqueueAction(OSCActions.START_SESSION);
            EnqueueAction(OSCActions.UPGRADE_API);
            EnqueueAction(OSCActions.SET_OPTIONS);
            mCurrentState = OSCStates.DISCONNECTED;
        }
        else
        {
            mCurrentState = OSCStates.IDLE;
        }
    }

    /**
     * When disconnected we can only received the result of a startSession command
     * Go to IDLE state
     **/
    void ManageDisconnected(JsonData jdata)
    {
        mInternalData.sessionId = jdata["results"]["sessionId"].ToString();
        mCurrentState = OSCStates.IDLE;
    }

    /**
     * Usually do nothing special when IDLE, just wait
     **/
    void ManageIdle(JsonData jdata)
    {
    }

    /**
     * LIVE PREVIEW: store the latest image
     **/
    void ManageLivePreview()
    {
        mBuffer = mHTTP.mStreamRequest.GetLastReceivedImage();
    }

    /**
     * When taking a photo we ask for operation progress until state is done
     * Then go to DOWNLOAD_PHOTO
     **/
    void ManageTakePhoto(JsonData jdata)
    {
        string state = jdata["state"].ToString();
        if (state == "inProgress")
        {
            mInternalData.currentOperationId = jdata["id"].ToString();
            EnqueueAction(OSCActions.PROGRESS_STATUS);
        }
        else if (state == "done")
        {
            mInternalData.fileURL = jdata["results"]["fileUrl"].ToString();
            EnqueueAction(OSCActions.DOWNLOAD);
            mCurrentState = OSCStates.DOWNLOAD_PHOTO;
        }
    }

    /**
     * Save photo as byte array and go to DELETE_PHOTO
     **/
    void ManageDownload(JsonData jdata)
    {
        mBuffer = mHTTP.GetRawResponse();
        EnqueueAction(OSCActions.DELETE);
        mCurrentState = OSCStates.DELETE_PHOTO;
    }

    /**
     * After delete call the callback to inform that we are done
     * Go to IDLE state
     **/
    void ManageDelete(JsonData jdata)
    {
        if (mCallBack != null)
        {
            mCallBack();
            mCallBack = null;
        }
        mCurrentState = OSCStates.IDLE;
    }


    /* After this line all methods are actions to be enqueued */

    /**
     * Starts a new session
     **/
    private void AskStartSession()
    {
        mHTTP.ChangeCommand(HttpRequest.Commands.POST_C_EXECUTE);
        mHTTP.SetJSONData(ConstructStartSessionJSONString());
        mHTTP.Execute();
    }

    private string ConstructStartSessionJSONString()
    {
        StringBuilder sb = new StringBuilder();
        JsonWriter json = new JsonWriter(sb);
        json.WriteObjectStart();
        json.WritePropertyName("name");
        json.Write("camera.startSession");
        json.WriteObjectEnd();

        return sb.ToString();
    }

    /**
     * Upgrades from API 2.0 to API 2.1
     **/
    private void AskUpgradeAPI()
    {
        mHTTP.ChangeCommand(HttpRequest.Commands.POST_C_EXECUTE);
        mHTTP.SetJSONData(ConstructUpgradeAPIJSONString());
        mHTTP.Execute();
    }

    private string ConstructUpgradeAPIJSONString()
    {
        StringBuilder sb = new StringBuilder();
        JsonWriter json = new JsonWriter(sb);
        json.WriteObjectStart();
        json.WritePropertyName("name");
        json.Write("camera.setOptions");
        json.WritePropertyName("parameters");
        json.WriteObjectStart();
        json.WritePropertyName("sessionId");
        json.Write(mInternalData.sessionId);
        json.WritePropertyName("options");
        json.WriteObjectStart();
        json.WritePropertyName("clientVersion");
        json.Write(2);
        json.WriteObjectEnd();
        json.WriteObjectEnd();
        json.WriteObjectEnd();

        return sb.ToString();
    }

    /**
     * Sets camera options
     **/
    private void AskSetOptions()
    {
        mHTTP.ChangeCommand(HttpRequest.Commands.POST_C_EXECUTE);
        mHTTP.SetJSONData(ConstructSetOptionsJSONString());
        mHTTP.Execute();
    }

    private string ConstructSetOptionsJSONString()
    {
        StringBuilder sb = new StringBuilder();
        JsonWriter json = new JsonWriter(sb);
        json.WriteObjectStart();
        json.WritePropertyName("name");
        json.Write("camera.setOptions");
        json.WritePropertyName("parameters");
        json.WriteObjectStart();
        json.WritePropertyName("options");
        json.WriteObjectStart();
        json.WritePropertyName("offDelay");
        json.Write(65535);
        json.WritePropertyName("sleepDelay");
        json.Write(65535);
        json.WritePropertyName("fileFormat");
        json.WriteObjectStart();
        json.WritePropertyName("type");
        json.Write("jpeg");
        json.WritePropertyName("width");
        json.Write(5376);
        json.WritePropertyName("height");
        json.Write(2688);
        json.WriteObjectEnd();
        json.WriteObjectEnd();
        json.WriteObjectEnd();
        json.WriteObjectEnd();

        return sb.ToString();
    }

    /**
     * Takes a picture
     **/
    private void AskTakePicture()
    {
        mHTTP.ChangeCommand(HttpRequest.Commands.POST_C_EXECUTE);
        mHTTP.SetJSONData(ConstructTakePictureJSONString());
        mCurrentState = OSCStates.TAKE_PHOTO;
        mHTTP.Execute();
    }

    private string ConstructTakePictureJSONString()
    {
        StringBuilder sb = new StringBuilder();
        JsonWriter json = new JsonWriter(sb);
        json.WriteObjectStart();
        json.WritePropertyName("name");
        json.Write("camera.takePicture");
        json.WritePropertyName("parameters");
        json.WriteObjectStart();
        json.WriteObjectEnd();
        json.WriteObjectEnd();

        return sb.ToString();
    }

    /**
     * Retrieves advancement of current operation
     **/
    private void AskProgressStatus()
    {
        mHTTP.ChangeCommand(HttpRequest.Commands.POST_C_STATUS);
        mHTTP.SetJSONData(ConstructProgressStatusJSONString());
        mHTTP.Execute();
    }

    private string ConstructProgressStatusJSONString()
    {
        StringBuilder sb = new StringBuilder();
        JsonWriter json = new JsonWriter(sb);
        json.WriteObjectStart();
        json.WritePropertyName("id");
        json.Write(mInternalData.currentOperationId);
        json.WriteObjectEnd();

        return sb.ToString();
    }

    /**
     * Downloads a photo
     **/
    private void AskDownloadPhoto()
    {
        mHTTP.ChangeCommand(mInternalData.fileURL);
        mHTTP.Execute();
    }

    /**
     * Deletes a photo
     **/
    private void AskDeletePhoto()
    {
        mHTTP.ChangeCommand(HttpRequest.Commands.POST_C_EXECUTE);
        mHTTP.SetJSONData(ConstructDeletePhotoJSONString());
        mHTTP.Execute();
    }

    private string ConstructDeletePhotoJSONString()
    {
        StringBuilder sb = new StringBuilder();
        JsonWriter json = new JsonWriter(sb);
        json.WriteObjectStart();
        json.WritePropertyName("name");
        json.Write("camera.delete");
        json.WritePropertyName("parameters");
        json.WriteObjectStart();
        json.WritePropertyName("fileUrls");
        json.WriteArrayStart();
        json.Write(mInternalData.fileURL);
        json.WriteArrayEnd();
        json.WriteObjectEnd();
        json.WriteObjectEnd();

        return sb.ToString();
    }

    /**
     * Starts live preview mode
     **/
    private void AskStartLivePreview()
    {
        mCurrentState = OSCStates.LIVE_PREVIEW;
        mHTTP.ChangeCommand(HttpRequest.Commands.POST_C_EXECUTE);
        mHTTP.SetJSONData(ConstructStartLivePreviewJSONString());
        mHTTP.NextRequestIsStream();
        mHTTP.Execute();
    }

    private string ConstructStartLivePreviewJSONString()
    {
        StringBuilder sb = new StringBuilder();
        JsonWriter json = new JsonWriter(sb);
        json.WriteObjectStart();
        json.WritePropertyName("name");
        json.Write("camera.getLivePreview");
        json.WriteObjectEnd();

        return sb.ToString();
    }

    /**
     * Gets camera current state
     **/
    private void AskCameraState()
    {
        mHTTP.ChangeCommand(HttpRequest.Commands.POST_STATE);
        mHTTP.SetJSONData("{}");
        mHTTP.Execute();
    }
}


/**
 * This structure holds data about a camera
 **/
struct osc_controller_data
{
    public string sessionId;            //session ID used before upgrading camera API to 2.1
    public string currentOperationId;   //ID of operation currently in progress in the camera
    public string fileURL;              //URL of file on the camera
    public bool isBusy;                 //is the camera actually handling a request
    public int remainingConnectionTry;  //number of retry before going to maintenance state
}
