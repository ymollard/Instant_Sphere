﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/**
* This class handle which application screen is being shown
* It is also responsible for calling OSC controller methods
**/
public sealed class ScreensController : MonoBehaviour
{
    //Unity components set in inspector
    public List<Canvas> mScreens;
    public Image mCountDownImg;
    public CameraRotation mCamera;
    public OSCController mOSCController;
    public SkyboxManager mSkyboxMng;
    public Watermark mWatermarker;
    public Sharing partage;
    //one state per screen
    public enum ScreensStates { WELCOME = 0, READY_TAKE_PHOTO, TAKING_PHOTO, WAITING, DISPLAY_PHOTO, DISPLAY_PHOTO_WITHOUT_INTERNET, SHARE_PHOTO, ERROR, PHOTO_CODE, GOODBYE };
    ScreensStates mCurrentState;

    //interface buttons
    enum InterfaceButtons { TAKE_PHOTO = 0, ABORT, RETRY, SHARE, BACK, SHARE_FB, SHARE_CODE, SHARE_EMAIL_OK, BUTTON_ENUM_END };
    bool[] mButtonsActivated = new bool[(int)InterfaceButtons.BUTTON_ENUM_END]; //buffer

    bool mIsOSCReady = false;
    FacebookConnector mFB;
    WifiManager mWifi;
    byte[] mFullResolutionImage;

    //count down used when taking a photo
    CounterDown mCounter = new CounterDown();
    Coroutine mCounterCoroutine;

    Timeout mTimeout;
    Coroutine mTimeoutCoroutine;
    const float mTimeoutValue = 60.0f;

    // Logs
    int mErrorCount = 0;

    /* Use this for initialization */
    private void Start()
    {
        mWifi = new WifiManager();
        if (!mWifi.WaitForWifi())                       //ensure that wifi is ON when app starts or quit
        {
            Application.Quit();
        }
        mFB = new FacebookConnector();
        mTimeout = new Timeout(mTimeoutValue, TimeoutGoToWelcome);
        Screen.sleepTimeout = SleepTimeout.NeverSleep;  //device screen should never turn off
        mCurrentState = ScreensStates.WELCOME;          //start application on welcome screen
        mCamera.AutomaticRotation(mTimeout);   //use automatic rotation of welcome photo
        UpdateScreen();
    }

    /* Update is called once per frame */
    private void Update()
    {
        if (!mOSCController.IsCameraOK())   //go to error state and stay inside
        {
            // For logs
            if (mErrorCount < 1)
            {
                Logger.Instance.WriteError(mCurrentState);
                mErrorCount++;
            }
            mCamera.StopRotation();
            mCurrentState = ScreensStates.ERROR;
            UpdateScreen();
        }
        else
        {
            //handle user interactions
            ManageStates();
            ResetButtons();
        }
    }

    /**
     * Method to be used as callback when "no action" timeout
     **/
    public void TimeoutGoToWelcome()
    {
        Logger.Instance.WriteTimeout();
        mOSCController.StopLivePreview();
        if (mTimeout != null)
            StopCoroutine(mTimeoutCoroutine);
        if (mCurrentState == ScreensStates.ERROR)
        {
            //mOSCController.RebootController();
            return;
        }
        mSkyboxMng.ResetSkybox();
        mTimeout = new Timeout(mTimeoutValue, TimeoutGoToWelcome);
        mCamera.AutomaticRotation(mTimeout);
        mCurrentState = ScreensStates.WELCOME;
        UpdateScreen();
    }

    /**
     * Method to be used as callback to signal that photo is ready
     **/
    public void TriggerOSCReady()
    {
        mIsOSCReady = true;
    }

    /* Theses are public methods called when user presses the corresponding button */
    public void ButtonTakePhoto()
    {
        SetButtonDown(InterfaceButtons.TAKE_PHOTO);
    }

    public void ButtonAbort()
    {
        SetButtonDown(InterfaceButtons.ABORT);
    }

    public void ButtonRetry()
    {
        SetButtonDown(InterfaceButtons.RETRY);
    }

    public void ButtonShare()
    {
        SetButtonDown(InterfaceButtons.SHARE);
    }

    public void ButtonBack()
    {
        SetButtonDown(InterfaceButtons.BACK);
    }

    public void ButtonShareFB()
    {
        SetButtonDown(InterfaceButtons.SHARE_FB);
    }

    public void ButtonShareCode()
    {
        SetButtonDown(InterfaceButtons.SHARE_CODE);
    }

    public void ButtonShareEmailOK()
    {
        SetButtonDown(InterfaceButtons.SHARE_EMAIL_OK);
    }

    /**
     * Memorize that button b has been triggered
     **/
    private void SetButtonDown(InterfaceButtons b)
    {
        mButtonsActivated[(int)b] = true;
    }

    /**
     * Return true if button b has been pressed since last buffer reset
     **/
    private bool IsButtonDown(InterfaceButtons b)
    {
        return mButtonsActivated[(int)b];
    }

    /**
     * Reset buttons buffer
     **/
    private void ResetButtons()
    {
        for (int i = 0; i < mButtonsActivated.Length; ++i)
            mButtonsActivated[i] = false;
    }

    /**
     * Change counter text on screen and set it to v
     **/
    private void UpdateCountDownText(int v)
    {
        string imageLocation = "ecran_3/";
        switch (v)
        {
            case 3:
                imageLocation += "ecran_3_A";
                break;
            case 2:
                imageLocation += "ecran_3_B";
                break;
            case 1:
                imageLocation += "ecran_3_C";
                break;
        }
        Texture2D s = Resources.Load(imageLocation) as Texture2D;
        Destroy(mCountDownImg.sprite);
        mCountDownImg.sprite = Sprite.Create(s, mCountDownImg.sprite.rect, new Vector2(0.5f, 0.5f));
    }

    /**
     * Update the shown canvas according to current state
     **/
    private void UpdateScreen()
    {
        foreach (Canvas s in mScreens)
            s.gameObject.SetActive(false);

        mScreens[(int)mCurrentState].gameObject.SetActive(true);
    }

    /**
     * Manage screens using internal state
     **/
    private void ManageStates()
    {
        switch (mCurrentState)
        {
            case ScreensStates.WELCOME:
                ManageWelcomeScreen();
                break;
            case ScreensStates.READY_TAKE_PHOTO:
                ManageReadyTakePhotoScreen();
                break;
            case ScreensStates.TAKING_PHOTO:
                ManageTakingPhotoScreen();
                break;
            case ScreensStates.WAITING:
                ManageWaitingScreen();
                break;
            case ScreensStates.DISPLAY_PHOTO:
            case ScreensStates.DISPLAY_PHOTO_WITHOUT_INTERNET:
                ManageDisplayScreen();
                break;
            case ScreensStates.SHARE_PHOTO:
                ManageShareScreen();
                break;
            case ScreensStates.PHOTO_CODE:
                break;
            case ScreensStates.GOODBYE:
                break;
        }
    }

    /**
     * If user touch the welcome screen stop automatic rotation
     * and go to take a photo screen
     **/
    private void ManageWelcomeScreen()
    {
        mCamera.AutomaticRotation(mTimeout);
        if (Input.touchCount > 0 || Input.GetMouseButton(0))
        {
            try
            {
                mOSCController.StartLivePreview();
                mCamera.AutomaticRotation(mTimeout);
                mTimeoutCoroutine = StartCoroutine(mTimeout.StartTimer());

                // For logs (new log)
                mErrorCount = 0;
                Logger.Instance.WriteStart();

                mCurrentState = ScreensStates.READY_TAKE_PHOTO;
                UpdateScreen();
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
            }
        }
    }

    /**
     * If user pushes the Take a Photo button go to screen taking photo
     **/
    private void ManageReadyTakePhotoScreen()
    {

        byte[] data = mOSCController.GetLatestData();
        Logger.Instance.ChangeToRT();
        if (data != null)
            mSkyboxMng.DefineNewSkybox(data);

        if (IsButtonDown(InterfaceButtons.TAKE_PHOTO))
        {
            mTimeout.Reset();

            // For logs
            Logger.Instance.WriteCapture();

            mCurrentState = ScreensStates.TAKING_PHOTO;
            UpdateScreen();
        }
    }

    /**
     * Start the countdown if it's not
     * If the countdown is finished, ask camera to capture a photo and go to waiting screen
     **/
    private void ManageTakingPhotoScreen()
    {

        byte[] data = mOSCController.GetLatestData();
        Logger.Instance.ChangeToHQ();

        if (data != null)
            mSkyboxMng.DefineNewSkybox(data);

        if (!mCounter.IsCounting())
        {
            mCounterCoroutine = StartCoroutine(mCounter.Count(3, UpdateCountDownText));
        }
        else if (mCounter.IsCounterFinished())
        {
            try
            {
                mTimeout.Reset();

                mIsOSCReady = false;
                mOSCController.StopLivePreview();
                mOSCController.StartCapture(TriggerOSCReady);
                mCurrentState = ScreensStates.WAITING;
                UpdateScreen();
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
            }
            finally
            {
                StopCoroutine(mCounterCoroutine);
                mCounter = new CounterDown();   //new countdown for next time
            }
        }
    }

    /**
     * Wait until the OSC controller signal that the photo is ready
     * Then retrieve the data, save them and go to display screen with automatic rotation
     **/
    private void ManageWaitingScreen()
    {
        if (mIsOSCReady)
        {
            mTimeout.Reset();
            mFullResolutionImage = mOSCController.GetLatestData();
            //mWatermarker.CreateWatermark(mFullResolutionImage);
            //mWatermarker.AddWatermark();
            //mFullResolutionImage = mWatermarker.GetBytes();
            mCamera.AutomaticRotation(mTimeout);

            //		    // Save image with watermark
            //		    var bytes = watermark.GetTexture().EncodeToPNG();
            //		    File.WriteAllBytes(Application.dataPath + "/final_picture.png", bytes);

            mSkyboxMng.DefineNewSkybox(mFullResolutionImage/*mWatermarker.GetTexture()*/);
            if (true /* TODO ping OK */)
                mCurrentState = ScreensStates.DISPLAY_PHOTO;
            else
                mCurrentState = ScreensStates.DISPLAY_PHOTO_WITHOUT_INTERNET;
            UpdateScreen();
        }
    }

    /**
     * On the display screen go to the share screen when user presses the OK button
     * Go to back to take a photo if user presses the RETRY button
     * Go to welcome screen if user presses the ABORT button
     **/
    private void ManageDisplayScreen()
    {
        if (IsButtonDown(InterfaceButtons.ABORT))
        {

            mTimeout.Reset();
            StopCoroutine(mTimeoutCoroutine);

            // For logs
            Logger.Instance.WriteVisualizeAbandon();

            mSkyboxMng.ResetSkybox();
            mCurrentState = ScreensStates.WELCOME;
            mCamera.AutomaticRotation(mTimeout);
        }
        else if (IsButtonDown(InterfaceButtons.RETRY))
        {
            mTimeout.Reset();

            // For logs
            Logger.Instance.WriteVisualizeRestart();
            mOSCController.StartLivePreview();
            mCurrentState = ScreensStates.READY_TAKE_PHOTO;
        }
        else if (IsButtonDown(InterfaceButtons.SHARE))
        {
            mTimeout.Reset();

            // For logs
            Logger.Instance.WriteVisualizeShare();
            mCurrentState = ScreensStates.SHARE_PHOTO;
            Debug.Log("**************** ON VA APPELER SendToServer*********");
            partage.SendToServer(mFullResolutionImage);
        }
        else
            return;
        UpdateScreen();
    }

    /**
     * On the share screen go back to display screen if user presses the BACK button
     * and disconnect the user from Facebook
     **/
    private void ManageShareScreen()
    {
        if (IsButtonDown(InterfaceButtons.SHARE_FB))
        {
            mTimeout.Reset();
            //mWifi.SaveAndShutdownWifi();

            // For logs

            Logger.Instance.WriteShareFacebook();

            //mWifi.SaveAndShutdownWifi();
            mFB.StartConnection(mFullResolutionImage);
        }

        if (IsButtonDown(InterfaceButtons.BACK))
        {
            mTimeout.Reset();
            StopCoroutine(mTimeoutCoroutine);

            // For logs
            Logger.Instance.WriteShareAbandon();

            //mWifi.RestoreWifi();
            //Thread.Sleep(3000);
            //mOSCController.RebootController();
            mSkyboxMng.ResetSkybox();
            //mCamera.AutomaticRotation(mLog, mTimeout);
            mCurrentState = ScreensStates.WELCOME;
            UpdateScreen();
        }
    }
}

/**
 * Class to handle counter down through Unity callback
 **/
public class CounterDown
{
    bool mCountingDown = false;
    bool mCountDownEnded = false;

    /**
     * Start counting from start to 0(excluded) second. Calls update(i) each time
     * This is a coroutine
     **/
    public IEnumerator Count(int start, Action<int> update)
    {
        mCountingDown = true;
        for (int i = start; i > 0; i--)
        {
            update(i);
            yield return new WaitForSeconds(1.0f);
        }
        mCountDownEnded = true;
    }

    /**
     * Returns true if counter has started
     **/
    public bool IsCounting()
    {
        return mCountingDown;
    }

    /**
     * Returns true if counter has reached 0
     **/
    public bool IsCounterFinished()
    {
        return mCountDownEnded;
    }
}
