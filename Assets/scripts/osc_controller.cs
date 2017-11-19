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
public sealed class osc_controller : MonoBehaviour
{
    HttpRequest mHTTP = new HttpRequest();
    Queue<MethodInfo> mExecutionQueue = new Queue<MethodInfo>();    //queue for methods that should be executed
    osc_controller_data mInternalData;                              //structure holding ID's
    Action mCallBack = null;                                        //callback to signal that photo download is finish
    Material mDefaultSkybox;
    byte[] mBuffer;
    enum OSCStates { DISCONNECTED, IDLE, TAKE_PHOTO, DOWNLOAD_PHOTO, DELETE_PHOTO };
    OSCStates mCurrentState;

    /* Actions associated with the method name */
    enum OSCActions { START_SESSION = 0, UPGRADE_API, SET_OPTIONS, TAKE_PICTURE, DOWNLOAD, PROGRESS_STATUS, CAMERA_INFO, DELETE };
    string[] mActionsMethodName = { "AskStartSession", "AskUpgradeAPI", "AskSetOptions", "AskTakePicture", "AskDownloadPhoto", "AskProgressStatus", "AskCameraInfo", "AskDeletePhoto" };

    // Use this for initialization
    private void Start()
    {
        mDefaultSkybox = RenderSettings.skybox;
        mInternalData.mFileURL = "";
        mInternalData.mCurrentOperationId = "";
        mInternalData.mSessionId = "";
        mInternalData.mIsBusy = false;
        mCurrentState = OSCStates.DISCONNECTED;
        EnqueueAction(OSCActions.START_SESSION);
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
     * Get the last downloaded photo as byte buffer
     **/
    public byte[] GetLatestData()
    {
        return mBuffer;
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
     *  Remove all actions waiting in the queue
     **/
    private void ClearQueue()
    {
        mExecutionQueue.Clear();
    }

    // Update is called once per frame
    private void Update()
    {
        //Dequeue and invoke a new method if we are done with the previous request
        if (!mInternalData.mIsBusy && mExecutionQueue.Count > 0)
        {
            mExecutionQueue.Dequeue().Invoke(this, null);
            mInternalData.mIsBusy = true;
        }
        else if (mInternalData.mIsBusy && mHTTP.IsTerminated())  //else if the request is terminated and successful handle it in the FSM
        {
            string s = mHTTP.GetHTTPResponse();
            if (mHTTP.IsSuccessful())
                ResponseHandler(s);
            else
                HandleError(s);
            mInternalData.mIsBusy = false;
        }
    }

    /**
     * Handle error from the HTTP part
     **/
    private void HandleError(string err)
    {
        Debug.Log("HTTP error: " + err); //Log error message
        mCurrentState = OSCStates.DISCONNECTED; //assume deconnection after error and try again
        ClearQueue();
        EnqueueAction(OSCActions.START_SESSION);
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
            case OSCStates.DISCONNECTED:
                ManageDisconnected(jdata);
                break;
            case OSCStates.IDLE:
                ManageIdle(jdata);
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
     * When disconnected we can only received the result of a startSession command
     * Then upgrade API and set camera options
     * Go to IDLE state
     **/
    void ManageDisconnected(JsonData jdata)
    {
        mInternalData.mSessionId = jdata["results"]["sessionId"].ToString();
        EnqueueAction(OSCActions.UPGRADE_API);
        EnqueueAction(OSCActions.SET_OPTIONS);
        mCurrentState = OSCStates.IDLE;
    }

    /**
     * Usually do nothing special when IDLE, just wait
     **/
    void ManageIdle(JsonData jdata)
    {

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
            mInternalData.mCurrentOperationId = jdata["id"].ToString();
            EnqueueAction(OSCActions.PROGRESS_STATUS);
        }
        else if (state == "done")
        {
            mInternalData.mFileURL = jdata["results"]["fileUrl"].ToString();
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

        /*if (!System.IO.Directory.Exists("/sdcard/AEI"))
            System.IO.Directory.CreateDirectory("/sdcard/AEI");
        System.IO.File.WriteAllBytes("/sdcard/AEI/downloaded_image.jpg", image);

        byte[] loadedPicture = System.IO.File.ReadAllBytes("/sdcard/AEI/downloaded_image.jpg");
        Texture2D t = new Texture2D(2, 2);
        t.LoadImage(loadedPicture);

        // TODO : convert equirectangular to cubemap and don't create a new skybox from the shader "Skybox/Equirectangular"
        /*Cubemap cmap = new Cubemap(t.height, TextureFormat.RGB24, false);
        cmap.SetPixels(t.GetPixels(), CubemapFace.PositiveX);
        cmap.filterMode = FilterMode.Trilinear;
        cmap.Apply();*/

        /*Material m = new Material(Shader.Find("Skybox/Equirectangular"));
        m.SetTexture("_Tex", t);

        RenderSettings.skybox = m;*/

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
     * Recover camera information
     **/
    private void AskCameraInfo()
    {
        mHTTP.ChangeCommand(HttpRequest.Commands.GET_INFO);
        mHTTP.Execute();
    }

    /**
     * Start a new session
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
     * Upgrade from API 2.0 to API 2.1
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
        json.Write(mInternalData.mSessionId);
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
     * Set camera options
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
     * Take a picture
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
     * Retrieve advancement of current operation
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
        json.Write(mInternalData.mCurrentOperationId);
        json.WriteObjectEnd();

        return sb.ToString();
    }

    /**
     * Download a photo
     **/
    private void AskDownloadPhoto()
    {
        mHTTP.ChangeCommand(mInternalData.mFileURL);
        mHTTP.Execute();
    }

    /**
     * Delete a photo
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
        json.Write(mInternalData.mFileURL);
        json.WriteArrayEnd();
        json.WriteObjectEnd();
        json.WriteObjectEnd();

        return sb.ToString();
    }
}


/**
 * This structure holds data about a camera
 **/
struct osc_controller_data
{
    public string mSessionId;          //session ID used before upgrading camera API to 2.1
    public string mCurrentOperationId; //ID of operation currently in progress in the camera
    public string mFileURL;            //URL of file on the camera
    public bool mIsBusy;         //is the camera actually handling a request
}