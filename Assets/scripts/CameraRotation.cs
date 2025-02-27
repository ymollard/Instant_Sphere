﻿using System;
using UnityEngine;

/**
 * This class controls the camera rotation for both automatic and manual mode
 **/
public sealed class CameraRotation : MonoBehaviour
{
    float mTurnSpeed = 5.0f;
    Vector2 mDelta;
    enum ECameraState { MANUAL, AUTO, OFF};
    ECameraState mRotationMode = ECameraState.MANUAL;
    const float threshold = 2.0f; // value in degrees for camera rotation in automatic mode

    public Transform container; // camera container

    Timeout mScreenTimeout;

    //For logs
    DateTime mDate = DateTime.Now;

    /**
     * Moves the camera according to the current mode
     **/
    private void Update()
    {
        if (mRotationMode == ECameraState.MANUAL)
        {
            /* Tablet mode */
            if (Input.touchCount > 0)
            {
                //For logs
                if (DateTime.Now > mDate.AddSeconds(2))
                {
                    mDate = DateTime.Now;
                    Logger.Instance.WriteNavigate();
                }

                mScreenTimeout.Reset();
                Vector2 tmp = Input.GetTouch(0).deltaPosition;
                tmp /= 5.0f;
                mDelta.x = tmp.y;
                mDelta.y = -tmp.x;
            }
            /* PC mode */
            else if (Input.GetMouseButton(0))
            {
                //For logs
                if (DateTime.Now > mDate.AddSeconds(2))
                {
                    mDate = DateTime.Now;
                    Logger.Instance.WriteNavigate();
                }

                mScreenTimeout.Reset();
                mDelta.x = Input.GetAxis("Mouse Y") * 10.0f;
                mDelta.y = -Input.GetAxis("Mouse X") * 10.0f;
            }
        }
        else if (mRotationMode == ECameraState.AUTO)
        {
            mDelta = ComputeDelta();
            if (Input.GetMouseButton(0) || Input.touchCount > 0)
                ManualRotation();
        }


        Vector2 cam = Vector2.zero, cont = Vector2.zero;
        cam.y = mDelta.y;
        cont.x = mDelta.x;

        //This is made in order to avoid rotation on Z, just typing 0 on Zcoord isn’t enough
        //so the container is rotated around Y and the camera around X separately
        container.Rotate(cam * Time.deltaTime * mTurnSpeed);
        transform.Rotate(cont * Time.deltaTime * mTurnSpeed);

        mDelta = Vector2.zero;
    }

    /**
     * Computes the delta vector for automatic rotation
     **/
    private Vector2 ComputeDelta()
    {
        Vector2 d = Vector2.zero;
        d.y = -threshold;
        float actualRotation = transform.rotation.eulerAngles.x;

        if (actualRotation > threshold && (360.0f - actualRotation) > threshold)
            if (actualRotation > 180.0f)
                d.x = threshold;
            else
                d.x = -threshold;
        return d;
    }

    /**
     * Enable automatic rotation mode
     **/
    public void AutomaticRotation(Timeout screenTimeout)
    {
        mRotationMode = ECameraState.AUTO;
        mScreenTimeout = screenTimeout;
    }

    /**
     * Enable manual rotation mode
     **/
    public void ManualRotation()
    {
        mRotationMode = ECameraState.MANUAL;
    }

    /**
     * Disable rotation (user can't move the screen)
     **/
    public void StopRotation()
    {
        mRotationMode = ECameraState.OFF;
    }
}
