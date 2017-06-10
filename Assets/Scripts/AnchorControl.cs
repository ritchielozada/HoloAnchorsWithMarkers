// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
//
// Ritchie Lozada (rlozada@microsoft.com)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HoloToolkit.Unity;
using UnityEngine;
using UnityEngine.VR.WSA;
using UnityEngine.VR.WSA.Persistence;
using UnityEngine.VR.WSA.Sharing;
using HoloToolkit.Unity.InputModule;
using HoloToolkit.Unity.SpatialMapping;
using Vuforia;

public class AnchorControl : MonoBehaviour, IInputClickHandler, IHoldHandler
{
    public enum ControlState
    {
        WaitingForAnchorStore,
        LoadOrCreateAnchor,
        WaitingForAnchorLocation,
        Ready,
        PlaceAnchor,
        NoAnchor,
        ScanningForMarker,
        FoundMarker
    }

    public string ClientId = "Client";
    public string AnchorName;
    public TextMesh StatusText;
    public WorldAnchor worldAnchor;
    public GameObject anchoredObject;
    public float WaitTimeDelay = 1f;
    public float FailTimeDelay = 15f;
    public float HoldTimeWait = 0f;
    public int MinAnchorDataSize = 256;    

    private SpatialMappingManager spatialMappingManager;
    private WorldAnchorStore anchorStore;
    private bool foundAnchorInStore;
    private float WaitTime = 0;
    private float FailTime = 0;

    private bool isClickHold = false;
    private float HoldTimeTarget;

    private bool isNetworked = false;
    private bool isNewAnchor = false;
    private List<byte> anchorBytes = new List<byte>();

    private Vector3 savePos;
    private Quaternion saveRot;
    private Material objectMat;

    private void DisplayStatus(string msg)
    {
        if (StatusText)
        {
            StatusText.text += string.Format("{0}\n", msg);
        }
    }

    private ControlState currentState;
    public ControlState CurrentState
    {
        get { return currentState; }
        set
        {
            DisplayStatus(string.Format("State={0}", value.ToString()));
            switch (value)
            {
                case ControlState.WaitingForAnchorStore:
                    objectMat.color = Color.gray;
                    WaitTime = Time.time;
                    FailTime = Time.time + FailTimeDelay;
                    break;
                case ControlState.LoadOrCreateAnchor:
                    objectMat.color = Color.blue;
                    break;
                case ControlState.WaitingForAnchorLocation:
                    objectMat.color = Color.yellow;
                    WaitTime = Time.time;
                    FailTime = Time.time + FailTimeDelay;
                    break;
                case ControlState.Ready:
                    Debug.Log("State: Ready");
                    objectMat.color = Color.green;
                    break;
                case ControlState.PlaceAnchor:
                    objectMat.color = Color.cyan;
                    break;
                case ControlState.NoAnchor:
                    objectMat.color = Color.red;
                    break;
            }
            currentState = value;
        }
    }

    void Start()
    {
        worldAnchor = null;
        foundAnchorInStore = false;        
        objectMat = anchoredObject.GetComponentInChildren<Renderer>().material;
        spatialMappingManager = SpatialMappingManager.Instance;
        ClientId += "-" + Guid.NewGuid().ToString();
        if (StatusText)
        {
            StatusText.text = string.Empty;
        }

        // Vuforia Controls
        VuforiaBehaviour.Instance.enabled = false;

        // Conflicting Constructor in HoloToolKit WorldAnchorManager
        WorldAnchorStore.GetAsync(SetAnchorStore);          // DO NOT USE WITH HoloToolkit WorldAnchorManager
        CurrentState = ControlState.WaitingForAnchorStore;
    }

    /// <summary>
    /// Event Handler for  WorldAnchorStore.GetAsync
    /// DO NOT USE with HoloToolkit WorldAnchorManager
    /// </summary>
    /// <param name="store"></param>
    private void SetAnchorStore(WorldAnchorStore store)
    {
        if (store == null)
        {
            Debug.LogError("EVENT: Anchor Store NULL result");
            DisplayStatus("Anchor Store NULL RESULT");
        }
        else
        {
            Debug.Log("EVENT: Anchor Store Ready");
            DisplayStatus("Anchor Store Ready");
            anchorStore = store;
            CurrentState = ControlState.LoadOrCreateAnchor;
        }
    }

    private void WaitForAnchorStore()
    {
        if (Time.time > WaitTime)
        {
            Debug.Log("WaitForAnchorStore() WaitTime");
            DisplayStatus("WaitForAnchorStore() WaitTime");
            objectMat.color = Color.yellow;

            // Requires WorldAnchorManager Script on Scene
            //if (WorldAnchorManager.IsInitialized && WorldAnchorManager.Instance.AnchorStore != null)
            //{
            //    anchorStore = WorldAnchorManager.Instance.AnchorStore;
            //    CurrentState = ControlState.LoadOrCreateAnchor;
            //}
            WaitTime = Time.time + WaitTime;
        }

        if (Time.time > FailTime)
        {
            Debug.LogError("WaitForAnchorStore() FAILSAFE");
            DisplayStatus("WaitForAnchorStore() FAILSAFE");
            objectMat.color = Color.red;
            FailTime = Time.time + FailTimeDelay;
            WorldAnchorStore.GetAsync(SetAnchorStore);
        }
    }

    private void LoadOrCreateAnchor()
    {
        Debug.LogFormat("Anchor Store ID Count: {0}", anchorStore.anchorCount);
        DisplayStatus(string.Format("Anchor Store ID Count: {0}", anchorStore.anchorCount));
        foundAnchorInStore = false;
        foreach (var id in anchorStore.GetAllIds())
        {
            if (id.Equals(AnchorName))
            {
                foundAnchorInStore = true;
                break;
            }
        }

        if (foundAnchorInStore)
        {            
            DisplayStatus("Loading Found Anchor");
            worldAnchor = anchorStore.Load(AnchorName, anchoredObject);
        }
        else
        {         
            DisplayStatus("Creating NEW Anchor");
            worldAnchor = anchoredObject.AddComponent<WorldAnchor>();
        }

        isNewAnchor = true;
        worldAnchor.OnTrackingChanged += WorldAnchor_OnTrackingChanged;
        CurrentState = ControlState.WaitingForAnchorLocation;
    }

    private void WorldAnchor_OnTrackingChanged(WorldAnchor self, bool located)
    {
        if (located)
        {
            Debug.LogFormat("Anchor Location LOCKED - EVENT");
            DisplayStatus("Creating NEW Anchor");
        }
        else
        {
            Debug.LogFormat("Anchor Location NOT LOCATED - EVENT");
            DisplayStatus("Anchor Location NOT LOCATED - EVENT");
        }
    }

    private void WaitForAnchorLocationLock()
    {
        if (Time.time > WaitTime)
        {
            Debug.LogFormat("Time: {0}/{1} Waiting for Anchor Lock", Time.time, FailTime);
            DisplayStatus(string.Format("Time: {0}/{1} Waiting for Anchor Lock", Time.time, FailTime));
            if (worldAnchor.isLocated)
            {
                DisplayStatus("Anchor Location ISLOCATED");
                Debug.LogFormat("Anchor Location LOCKED");
                anchorStore.Save(AnchorName, worldAnchor);
                Debug.LogFormat("Anchor SAVED");

                CurrentState = ControlState.Ready;
            }
            else
            {
                if (Time.time > FailTime)
                {
                    Debug.LogError("Anchor Lock Timeout");
                    CurrentState = ControlState.NoAnchor;
                }
                else
                {
                    WaitTime = Time.time + WaitTimeDelay;
                }
            }
        }
    }

    private void DeleteObjectAnchor()
    {        
        if(worldAnchor != null)
        {
            worldAnchor.OnTrackingChanged -= WorldAnchor_OnTrackingChanged;
            DestroyImmediate(anchoredObject.GetComponent<WorldAnchor>());
            savePos = anchoredObject.transform.position;
            saveRot = anchoredObject.transform.rotation;
        }
    }

    public void PlaceAnchor()
    {
        if ((currentState == ControlState.Ready) || (currentState == ControlState.NoAnchor))
        {
            spatialMappingManager.DrawVisualMeshes = true;            
            DeleteObjectAnchor();
            CurrentState = ControlState.PlaceAnchor;
        }
        else
        {
            Debug.Log("Cannot Place Anchor, Anchors NOT Ready");
            DisplayStatus("Cannot Place Anchor, Anchors NOT Ready");
        }

    }

    public void SetAnchor()
    {
        if (currentState == ControlState.PlaceAnchor)
        {
            anchorStore.Delete(AnchorName);
            spatialMappingManager.DrawVisualMeshes = false;
            LoadOrCreateAnchor();
        }
        else
        {
            Debug.Log("Cannot set anchor, not in placement state");
            DisplayStatus("Cannot set anchor, not in placement state");
        }
    }

    public void CancelAnchor()
    {
        if (currentState == ControlState.PlaceAnchor)
        {
            anchoredObject.transform.position = savePos;
            anchoredObject.transform.rotation = saveRot;
            spatialMappingManager.DrawVisualMeshes = false;
            LoadOrCreateAnchor();
        }
        else
        {
            Debug.Log("Cannot cancel anchor, not in placement state");
            DisplayStatus("Cannot cancel anchor, not in placement state");
        }
    }

    private void TrackAnchoredObject()
    {
        Vector3 headPos = Camera.main.transform.position;
        Vector3 gazeDirection = Camera.main.transform.forward;

        RaycastHit hit;
        if (Physics.Raycast(headPos, gazeDirection, out hit, 30.0f, spatialMappingManager.LayerMask))
        {
            Quaternion targetRot = Camera.main.transform.localRotation;
            targetRot.x = 0;
            targetRot.z = 0;

            anchoredObject.transform.position = hit.point;
            anchoredObject.transform.rotation = targetRot;
        }
    }

    private void WriteBuffer(byte[] data)
    {
        anchorBytes.AddRange(data);
    }

    private void AnchorExportHandler(SerializationCompletionReason status)
    {
        int dataSize = anchorBytes.Count;

        Debug.LogFormat("AnchorControl: AnchorExportHandler()");
        DisplayStatus("AnchorControl: AnchorExportHandler()");

        if (status == SerializationCompletionReason.Succeeded)
        {
            if(dataSize > MinAnchorDataSize)
            {
                byte[] anchorDataBytes = null;

                AnchorName = ClientId;
                anchorDataBytes = anchorBytes.ToArray();

                // TODO: SEND anchorDataByes
                Debug.LogFormat("Anchor Data SEND: {0}", dataSize);
                
            }
            else
            {
                Debug.LogErrorFormat("Anchor Export Min Size Error {0}/{1}", dataSize, MinAnchorDataSize);
            }
        }
        else
        {
            Debug.LogErrorFormat("Anchor Export ERROR");
        }
    }

    private void AnchorImportHandler(SerializationCompletionReason status, WorldAnchorTransferBatch watb)    
    {
        if (status == SerializationCompletionReason.Succeeded)
        {
            // Load Imported Anchor
            Debug.Log("Anchor Import Handler");
            DeleteObjectAnchor();
            anchorStore.Delete(AnchorName);
            worldAnchor = watb.LockObject(AnchorName, anchoredObject);
            anchorStore.Save(AnchorName, worldAnchor);  // Force Save when sent as a located network anchor
            worldAnchor.OnTrackingChanged += WorldAnchor_OnTrackingChanged;            
            CurrentState = ControlState.WaitingForAnchorLocation;            
        }
        else
        {
            Debug.LogErrorFormat("AnchorControl: AnchorImportHander Error - {0}", status.ToString());
        }
    }

    private void ProcessNetworkAnchors()
    {
        // Send New Anchors
        if (isNewAnchor && worldAnchor != null) 
        {
            isNewAnchor = false;

            Debug.LogError("AnchorControl: ProcessNetworkAnchors");

            WorldAnchorTransferBatch watb = new WorldAnchorTransferBatch();
            if (watb.AddWorldAnchor(ClientId, worldAnchor))
            {
                WorldAnchorTransferBatch.ExportAsync(watb, WriteBuffer, AnchorExportHandler);
            }
            else
            {
                Debug.LogError("WorldAnchorTransferBatch-Add Anchor Error");
            }
        }
    }

    void Update()
    {
        switch (currentState)
        {
            case ControlState.WaitingForAnchorStore:
                WaitForAnchorStore();
                break;
            case ControlState.LoadOrCreateAnchor:
                LoadOrCreateAnchor();
                break;
            case ControlState.WaitingForAnchorLocation:
                WaitForAnchorLocationLock();
                break;
            case ControlState.Ready:
                if (isNetworked)
                {                    
                    ProcessNetworkAnchors();
                }
                break;
            case ControlState.PlaceAnchor:
                TrackAnchoredObject();
                break;
        }

        if (isClickHold && Time.time > HoldTimeTarget)
        {
            Debug.LogFormat("CLICK HOLD EVENT! {0}", Time.time);
            DisplayStatus(string.Format(">>> Click-Hold {0}", Time.time));
            isClickHold = false;
        }
    }

    public void OnInputClicked(InputClickedEventData eventData)
    {
        Debug.LogFormat("Click Event: {0}", Time.time);
        DisplayStatus(string.Format("Click Event: {0}", Time.time));
        if (currentState == ControlState.PlaceAnchor)
        {
            SetAnchor();
        }
    }

    public void OnHoldStarted(HoldEventData eventData)
    {
        Debug.LogFormat("Hold Event Started: {0}", Time.time);
        DisplayStatus(string.Format("Hold Event Started: {0}", Time.time));
        isClickHold = true;
        HoldTimeTarget = Time.time + HoldTimeWait;

        if (currentState == ControlState.PlaceAnchor)
        {
            CancelAnchor();
        }
        else
        {
            PlaceAnchor();
        }
    }

    public void OnHoldCompleted(HoldEventData eventData)
    {
        isClickHold = false;
    }

    public void OnHoldCanceled(HoldEventData eventData)
    {
        isClickHold = false;
    }


    // Called by NetworkControl to load new Anchor
    public bool ImportAnchorData(byte[] anchorData)
    {        
        if (anchorStore != null)
        {
            Debug.Log("Importing Anchor");
            if (currentState == ControlState.PlaceAnchor)
            {
                CancelAnchor();
            }
            WorldAnchorTransferBatch.ImportAsync(anchorData, AnchorImportHandler);            
            return true;
        }
        else
        {
            Debug.LogError("Importing Anchor: Anchor Store Not Ready");
            return false;
        }
    }

    public void ClearStatusText()
    {
        if (StatusText)
        {
            StatusText.text = string.Empty;
        }
    }

    // Vuforia Marker Detection

    public void ScanForMarker()
    {
        if ((currentState == ControlState.Ready) || (currentState == ControlState.NoAnchor))
        {
            DisplayStatus("Scanning for Marker");
            DeleteObjectAnchor();
            CurrentState = ControlState.ScanningForMarker;
            VuforiaBehaviour.Instance.enabled = true;
        }
        else
        {
            DisplayStatus("State Not Ready for Scanning Marker");
        }
    }

    public void FoundMarker(Vector3 pos, Quaternion rot)
    {
        if (currentState == ControlState.ScanningForMarker)
        {
            DisplayStatus("Found Marker");
            CurrentState = ControlState.FoundMarker;
            VuforiaBehaviour.Instance.enabled = false;
            anchorStore.Delete(AnchorName);
            anchoredObject.transform.position = pos;
            anchoredObject.transform.rotation = rot;
            LoadOrCreateAnchor();
        }
        else
        {
            DisplayStatus("ERROR: Found Marker Triggered while not scanning");
        }
    }

    public void CancelScanForMarker()
    {
        if (currentState == ControlState.ScanningForMarker)
        {
            DisplayStatus("Cancel Scanning for Marker");
            VuforiaBehaviour.Instance.enabled = false;
            anchoredObject.transform.position = savePos;
            anchoredObject.transform.rotation = saveRot;
            LoadOrCreateAnchor();
        }
        else
        {
            DisplayStatus("ERROR: Cancel Marker Triggered while not scanning");
        }
    }
}
