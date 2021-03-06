﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Linq;

using UnityEngine;
using KSP;
using KSPPluginFramework;

using Contracts;

namespace KerbalAlarmClock
{
    public partial class KerbalAlarmClock
    {
        private KACAlarm.AlarmTypeEnum AddType = KACAlarm.AlarmTypeEnum.Raw;
        private KACAlarm.AlarmActionEnum AddAction = KACAlarm.AlarmActionEnum.MessageOnly;

        private KACTimeStringArray timeRaw = new KACTimeStringArray(600, KACTimeStringArray.TimeEntryPrecisionEnum.Hours);
        private KACTimeStringArray timeMargin = new KACTimeStringArray(KACTimeStringArray.TimeEntryPrecisionEnum.Hours);
        private KACTimeStringArray timeRepeatPeriod = new KACTimeStringArray(50 * KSPDateStructure.SecondsPerDay, KACTimeStringArray.TimeEntryPrecisionEnum.Days);

        private String strAlarmName = "";
        private String strAlarmNotes = "";
        //private String strAlarmNotes = "";
        //private String strAlarmDetail = "";
        private Boolean blnAlarmAttachToVessel=true;

        private String strAlarmDescSOI = "This will monitor the current active flight path for the next detected SOI change.\r\n\r\nIf the SOI Point changes the alarm will adjust until it is within {0} seconds of the Alarm time, at which point it just maintains the last captured time of the change.";
        private String strAlarmDescXfer = "This will check and recalculate the active transfer alarms for the correct phase angle - the math for these is based around circular orbits so the for any elliptical orbit these need to be recalculated over time.\r\n\r\nThe alarm will adjust until it is within {0} seconds of the target phase angle, at which point it just maintains the last captured time of the angle.\r\nI DO NOT RECOMMEND TURNING THIS OFF UNLESS THERE IS A MASSIVE PERFORMANCE GAIN";
        private String strAlarmDescNode = "This will check and recalculate the active orbit node alarms as the flight path changes. The alarm will adjust until it is within {0} seconds of the node.";

        private string strAlarmDescMan = "Will create an alarm whenever a maneuver node is detected on the vessels flight plan\r\n\r\nIf the Man Node is within {0} seconds of the current time it will not be created";

        /// <summary>
        /// Code to reset the settings etc when the new button is hit
        /// </summary>
        private void NewAddAlarm()
        {
            //Set time variables
            timeRaw.BuildFromUT(600);
            strRawUT = "";
            _ShowAddMessages = false;

            //option for xfer mode
            if (settings.XferUseModelData)
                intXferType = 0;
            else
                intXferType = 1;

            //default margin
            timeMargin.BuildFromUT(settings.AlarmDefaultMargin);

            //set default strings
            if (KACWorkerGameState.CurrentVessel != null)
                strAlarmName = KACWorkerGameState.CurrentVessel.vesselName + "";
            else
                strAlarmName = "No Vessel";
            strAlarmNotes = "";
            AddNotesHeight = 100;

            AddAction = (KACAlarm.AlarmActionEnum)settings.AlarmDefaultAction;
            //blnHaltWarp = true;

            //set initial alarm type based on whats on the flight path
            if (KACWorkerGameState.ManeuverNodeExists)
                AddType = KACAlarm.AlarmTypeEnum.Maneuver;//AddAlarmType.Node;
            else if (KACWorkerGameState.SOIPointExists)
                AddType = KACAlarm.AlarmTypeEnum.SOIChange;//AddAlarmType.Node;
            else
                AddType = KACAlarm.AlarmTypeEnum.Raw;//AddAlarmType.Node;

            //trigger the work to set each type
            AddTypeChanged();

            //build the XFer parents list
            SetUpXferParents();
            intXferCurrentParent = 0;
            SetupXferOrigins();
            intXferCurrentOrigin = 0;

            if (KACWorkerGameState.CurrentVessel != null)
            {
                //if the craft is orbiting a body on the parents list then set it as the default
                if (XferParentBodies.Contains(KACWorkerGameState.CurrentVessel.mainBody.referenceBody))
                {
                    intXferCurrentParent = XferParentBodies.IndexOf(KACWorkerGameState.CurrentVessel.mainBody.referenceBody);
                    SetupXferOrigins();
                    intXferCurrentOrigin = XferOriginBodies.IndexOf(KACWorkerGameState.CurrentVessel.mainBody);
                }
            }
            //set initial targets
            SetupXFerTargets();
            intXferCurrentTarget = 0;

            intSelectedCrew = 0;
            intSelectedContract = 0;
            strCrewUT = "";
        }

        List<KACAlarm.AlarmTypeEnum> AlarmsThatBuildStrings = new List<KACAlarm.AlarmTypeEnum>() {
            KACAlarm.AlarmTypeEnum.Raw,
            KACAlarm.AlarmTypeEnum.Transfer,
            KACAlarm.AlarmTypeEnum.TransferModelled,
            KACAlarm.AlarmTypeEnum.Crew,
            KACAlarm.AlarmTypeEnum.Contract,
            KACAlarm.AlarmTypeEnum.ContractAuto,
        };

        private String strAlarmEventName = "Alarm";
        internal void AddTypeChanged()
        {
            if (AddType == KACAlarm.AlarmTypeEnum.Transfer || AddType == KACAlarm.AlarmTypeEnum.TransferModelled)
                blnAlarmAttachToVessel = false;
            else
                blnAlarmAttachToVessel = true;

            //set strings, etc here for type changes
            switch (AddType)
            {
                case KACAlarm.AlarmTypeEnum.Raw: strAlarmEventName = "Alarm"; break;
                case KACAlarm.AlarmTypeEnum.Maneuver: strAlarmEventName = "Node"; break;
                case KACAlarm.AlarmTypeEnum.SOIChange: strAlarmEventName = "SOI"; break;
                case KACAlarm.AlarmTypeEnum.Transfer:
                case KACAlarm.AlarmTypeEnum.TransferModelled: strAlarmEventName = "Transfer"; break;
                case KACAlarm.AlarmTypeEnum.Apoapsis: strAlarmEventName = "Apoapsis"; break;
                case KACAlarm.AlarmTypeEnum.Periapsis: strAlarmEventName = "Periapsis"; break;
                case KACAlarm.AlarmTypeEnum.AscendingNode: strAlarmEventName = "Ascending"; break;
                case KACAlarm.AlarmTypeEnum.DescendingNode: strAlarmEventName = "Descending"; break;
                case KACAlarm.AlarmTypeEnum.LaunchRendevous: strAlarmEventName = "Launch Ascent"; break;
                case KACAlarm.AlarmTypeEnum.Closest: strAlarmEventName = "Closest"; break;
                case KACAlarm.AlarmTypeEnum.Distance: strAlarmEventName = "Target Distance"; break;
                case KACAlarm.AlarmTypeEnum.Crew: strAlarmEventName = "Crew"; break;
                case KACAlarm.AlarmTypeEnum.Contract: 
                case KACAlarm.AlarmTypeEnum.ContractAuto: strAlarmEventName = "Contract"; break;
                default:
                    strAlarmEventName = "Alarm"; 
                    break;
            }

            //set strings, etc here for type changes
            strAlarmName = (KACWorkerGameState.CurrentVessel != null) ? KACWorkerGameState.CurrentVessel.vesselName : "Alarm";
            strAlarmNotes = "";
            if (KACWorkerGameState.CurrentVessel != null)
            {
                switch (AddType)
                {
                    case KACAlarm.AlarmTypeEnum.Raw:
                        BuildRawStrings();
                        break;
                    case KACAlarm.AlarmTypeEnum.Maneuver:
                        strAlarmNotes = "Time to pay attention to\r\n    " + strAlarmName + "\r\nNearing Maneuver Node";
                        break;
                    case KACAlarm.AlarmTypeEnum.SOIChange:
                        if (KACWorkerGameState.SOIPointExists)
                            strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing SOI Change\r\n" +
                                            "     Old SOI: " + KACWorkerGameState.CurrentVessel.orbit.referenceBody.bodyName + "\r\n" +
                                            "     New SOI: " + KACWorkerGameState.CurrentVessel.orbit.nextPatch.referenceBody.bodyName;
                        break;
                    case KACAlarm.AlarmTypeEnum.Transfer:
                    case KACAlarm.AlarmTypeEnum.TransferModelled:
                        BuildTransferStrings();
                        break;
                    case KACAlarm.AlarmTypeEnum.Apoapsis:
                        strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Apoapsis";
                        break;
                    case KACAlarm.AlarmTypeEnum.Periapsis:
                        strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Periapsis";
                        break;
                    case KACAlarm.AlarmTypeEnum.AscendingNode:
                        strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Ascending Node";
                        break;
                    case KACAlarm.AlarmTypeEnum.DescendingNode:
                        strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Descending Node";
                        break;
                    case KACAlarm.AlarmTypeEnum.LaunchRendevous:
                        strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Launch Rendevous";
                        break;
                    case KACAlarm.AlarmTypeEnum.Closest:
                        strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Closest Approach";
                        break;
                    case KACAlarm.AlarmTypeEnum.Distance:
                        strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Target Distance";
                        break;
                    case KACAlarm.AlarmTypeEnum.Crew:
                        BuildCrewStrings();
                        CrewAlarmStoreNode = settings.AlarmCrewDefaultStoreNode;
                        break;
                    case KACAlarm.AlarmTypeEnum.Contract:
                    case KACAlarm.AlarmTypeEnum.ContractAuto:
                        //BuildContractStringsAndMargin();
                        break;
                    default:
                        break;
                }
            }

            //If the type is a contract then theres some margin adjustments to do
            if (AddType == KACAlarm.AlarmTypeEnum.Contract || AddType == KACAlarm.AlarmTypeEnum.ContractAuto)
                BuildContractStringsAndMargin(true);
            else
                timeMargin.BuildFromUT(settings.AlarmDefaultMargin);
        }

        private void BuildTransferStrings()
        {
            String strWorking = "";
            if (blnAlarmAttachToVessel)
                strWorking = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Celestial Transfer:";
            else
                strWorking = "Nearing Celestial Transfer:";

            if (XferTargetBodies !=null && intXferCurrentTarget<XferTargetBodies.Count)
                strWorking += "\r\n\tOrigin: " + XferTargetBodies[intXferCurrentTarget].Origin.bodyName + "\r\n\tTarget: " + XferTargetBodies[intXferCurrentTarget].Target.bodyName;
            strAlarmNotes= strWorking;

            strWorking = "";
            if (XferTargetBodies != null && intXferCurrentTarget < XferTargetBodies.Count)
                strWorking = XferTargetBodies[intXferCurrentTarget].Origin.bodyName + "->" + XferTargetBodies[intXferCurrentTarget].Target.bodyName;
            else
                strWorking = strWorking = "Nearing Celestial Transfer";
            strAlarmName = strWorking;
        }

        private void BuildRawStrings()
        {
            String strWorking = "";
            if (blnAlarmAttachToVessel && KACWorkerGameState.CurrentVessel != null)
                strWorking = "Time to pay attention to:\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nRaw Time Alarm";
            else
                strWorking = "Raw Time Alarm";
            strAlarmNotes = strWorking;

            strWorking = "";
            if (blnAlarmAttachToVessel && KACWorkerGameState.CurrentVessel != null)
                strWorking = KACWorkerGameState.CurrentVessel.vesselName;
            else
                strWorking = "Raw Time Alarm";
            strAlarmName = strWorking;
        }

        private void BuildCrewStrings()
        {
            strAlarmEventName = "Crew";
            List<ProtoCrewMember> pCM = null;
            if (KACWorkerGameState.CurrentVessel != null)
                pCM = KACWorkerGameState.CurrentVessel.GetVesselCrew();
            if (pCM != null && pCM.Count == 0)
            {
                strAlarmName = "Crew member alarm";
                strAlarmNotes = "No Kerbals present in this vessel";
            } else {
                strAlarmName = pCM[intSelectedCrew].name;
                strAlarmNotes = string.Format("Alarm for {0}\r\nNot tied to any vessel - will follow the Kerbal", pCM[intSelectedCrew].name);
            }
        }

        private void BuildContractStringsAndMargin(Boolean ForceUpdateMargin=false)
        {
            strAlarmEventName = "Contract";
            if (ContractSystem.Instance == null || lstContracts.Count == 0)
            {
                strAlarmName = "Contract alarm";
                strAlarmNotes = "No Contracts Available";
            }
            else
            {
                GenerateContractStringsFromContract(lstContracts[intSelectedContract], out strAlarmName, out strAlarmNotes);


                if (ForceUpdateMargin || contractLastState != lstContracts[intSelectedContract].ContractState)
                {
                    if (lstContracts[intSelectedContract].ContractState == Contract.State.Active)
                        timeMargin.BuildFromUT(settings.AlarmOnContractDeadlineMargin);
                    else
                        timeMargin.BuildFromUT(settings.AlarmOnContractExpireMargin);

                    contractLastState = lstContracts[intSelectedContract].ContractState;
                }
            }
        }

        private void GenerateContractStringsFromContract(Contract c, out String AlarmName, out String AlarmNotes)
        {
            AlarmName = c.Title;
            AlarmNotes = String.Format("{0}\r\nName: {1}\r\nParameters:", c.AlarmType().Description(), c.Synopsys);
            foreach (ContractParameter cp in c.AllParameters)
            {
                AlarmNotes += String.Format("\r\n    * {0}", cp.Title);
            }
        }


        //String[] strAddTypes = new String[] { "Raw", "Maneuver","SOI","Transfer" };
        private String[] strAddTypes = new String[] { "R", "M", "A", "P", "A", "D", "S", "X" };

        private GUIContent[] guiTypes = new GUIContent[]
            {
                new GUIContent(KACResources.btnRaw,"Raw Time Alarm"),
                new GUIContent(KACResources.btnMNode,"Maneuver Node"),
                new GUIContent(KACResources.btnApPe,"Apoapsis / Periapsis"),
                //new GUIContent(KACResources.btnAp,"Apoapsis"),
                //new GUIContent(KACResources.btnPe,"Periapsis"),
                new GUIContent(KACResources.btnANDN,"Ascending/Descending Node"),
                //new GUIContent(KACResources.btnAN,"Ascending Node"),
                //new GUIContent(KACResources.btnDN,"Descending Node"),
                new GUIContent(KACResources.btnClosest,"Closest/Target Distance"),
                new GUIContent(KACResources.btnSOI,"SOI Change"),
                new GUIContent(KACResources.btnXfer,"Transfer Window"),
                new GUIContent(KACResources.btnCrew,"Kerbal Crew"),
                new GUIContent(KACResources.btnContract,"Contract")
            };

        private GUIContent[] guiTypesView = new GUIContent[]
            {
                new GUIContent(KACResources.btnRaw,"Raw Time Alarm")
            };

        private GUIContent[] guiTypesSpaceCenter = new GUIContent[]
            {
                new GUIContent(KACResources.btnRaw,"Raw Time Alarm"),
                new GUIContent(KACResources.btnXfer,"Transfer Window"),
                new GUIContent(KACResources.btnContract,"Contract")
            };
        private GUIContent[] guiTypesTrackingStation = new GUIContent[]
            {
                new GUIContent(KACResources.btnRaw,"Raw Time Alarm"),
                new GUIContent(KACResources.btnMNode,"Maneuver Node"),
                new GUIContent(KACResources.btnApPe,"Apoapsis / Periapsis"),
                new GUIContent(KACResources.btnSOI,"SOI Change"),
                new GUIContent(KACResources.btnXfer,"Transfer Window"),
                new GUIContent(KACResources.btnCrew,"Kerbal Crew"),
                new GUIContent(KACResources.btnContract,"Contract")
            };

        private GameScenes[] ScenesForAttachOption = new GameScenes[] 
            { 
                GameScenes.FLIGHT, 
                GameScenes.TRACKSTATION, 
            };

        private KACAlarm.AlarmTypeEnum[] TypesForAttachOption = new KACAlarm.AlarmTypeEnum[] 
            { 
                KACAlarm.AlarmTypeEnum.Raw, 
                KACAlarm.AlarmTypeEnum.Transfer, 
                KACAlarm.AlarmTypeEnum.TransferModelled,
                KACAlarm.AlarmTypeEnum.Contract, 
                KACAlarm.AlarmTypeEnum.ContractAuto
            };

        private KACAlarm.AlarmTypeEnum[] TypesWithNoEvent = new KACAlarm.AlarmTypeEnum[] 
            { 
                KACAlarm.AlarmTypeEnum.Raw, 
                KACAlarm.AlarmTypeEnum.Transfer, 
                KACAlarm.AlarmTypeEnum.TransferModelled 
            };

        private int intHeight_AddWindowCommon;
        private int intHeight_AddWindowRepeat;
        /// <summary>
        /// Draw the Add Window contents
        /// </summary>
        /// <param name="WindowID"></param>
        internal void FillAddWindow(int WindowID)
        {
            GUILayout.BeginVertical();

            //AddType =  (KACAlarm.AlarmType)GUILayout.Toolbar((int)AddType, strAddTypes,KACResources.styleButton);
            GUIContent[] guiButtons = guiTypes;
            //if (ViewAlarmsOnly) guiButtons = guiTypesView;
            switch (MonoName)
            {
                case "KACSpaceCenter":
                    guiButtons = guiTypesSpaceCenter; break;
                case "KACTrackingStation":
                    guiButtons = guiTypesTrackingStation; break;
                default:
                    break;
            }
            if (DrawButtonList(ref AddType,guiButtons))
            {
                //if the choice was the Ap/Pe one then work out the best next choice
                if(AddType== KACAlarm.AlarmTypeEnum.Apoapsis) {
                    
                    if(!KACWorkerGameState.ApPointExists && KACWorkerGameState.PePointExists)
                        AddType = KACAlarm.AlarmTypeEnum.Periapsis;
                    else if(KACWorkerGameState.ApPointExists && KACWorkerGameState.PePointExists &&
                            ((KACWorkerGameState.CurrentVessel == null) ? 0 : KACWorkerGameState.CurrentVessel.orbit.timeToAp) > ((KACWorkerGameState.CurrentVessel == null ) ? 0 : KACWorkerGameState.CurrentVessel.orbit.timeToPe))
                        AddType = KACAlarm.AlarmTypeEnum.Periapsis;
                }
                AddTypeChanged();
            }
            
            //if (AddType == KACAlarm.AlarmType.Apoapsis || AddType == KACAlarm.AlarmType.Periapsis)
            //    WindowLayout_AddTypeApPe();
            //if (AddType == KACAlarm.AlarmType.AscendingNode || AddType == KACAlarm.AlarmType.DescendingNode)
            //    WindowLayout_AddTypeANDN();

            //calc height for common stuff
            intHeight_AddWindowCommon = 64;
            if (AddType != KACAlarm.AlarmTypeEnum.Raw && AddType!= KACAlarm.AlarmTypeEnum.Crew) //add stuff for margins
                intHeight_AddWindowCommon += 28;
            if (ScenesForAttachOption.Contains(KACWorkerGameState.CurrentGUIScene) && TypesForAttachOption.Contains(AddType) && KACWorkerGameState.CurrentVessel != null) //add stuff for attach to ship
                    intHeight_AddWindowCommon += 30;
            if (KACWorkerGameState.CurrentGUIScene == GameScenes.TRACKSTATION)
                intHeight_AddWindowCommon += 18;

            //layout the right fields for the common components
            Boolean blnAttachPre = blnAlarmAttachToVessel;
            WindowLayout_CommonFields2(ref strAlarmName, ref blnAlarmAttachToVessel, ref AddAction, ref timeMargin, AddType, intHeight_AddWindowCommon);

            Double dblTimeToPoint = 0;

            //layout the specific pieces for each type of alarm
            switch (AddType)
            {
                case KACAlarm.AlarmTypeEnum.Raw:
                    if (blnAttachPre!=blnAlarmAttachToVessel) BuildRawStrings();
                    WindowLayout_AddPane_Raw();
                    break;
                case KACAlarm.AlarmTypeEnum.Maneuver:
                    WindowLayout_AddPane_Maneuver();
                    break;
                case KACAlarm.AlarmTypeEnum.SOIChange:
                    dblTimeToPoint = (KACWorkerGameState.CurrentVessel==null) ? 0 : KACWorkerGameState.CurrentVessel.orbit.UTsoi - KACWorkerGameState.CurrentTime.UT;
                    WindowLayout_AddPane_NodeEvent(KACWorkerGameState.SOIPointExists,dblTimeToPoint);
                    //WindowLayout_AddPane_SOI2();
                    break;
                case KACAlarm.AlarmTypeEnum.Transfer:
                case KACAlarm.AlarmTypeEnum.TransferModelled:
                    if (blnAttachPre != blnAlarmAttachToVessel) BuildTransferStrings();
                    WindowLayout_AddPane_Transfer();
                    break;
                case KACAlarm.AlarmTypeEnum.Apoapsis:
                    WindowLayout_AddTypeApPe();

                    if (!KACWorkerGameState.ApPointExists && HighLogic.CurrentGame.Mode == Game.Modes.CAREER &&
                        GameVariables.Instance.GetOrbitDisplayMode(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation)) != GameVariables.OrbitDisplayMode.PatchedConics)
                    {
                        GUILayout.Label("Apoapsis Alarms cannot be set without upgrading the Tracking Station", GUILayout.ExpandWidth(true));
                    }
                    else
                    {
                        dblTimeToPoint = (KACWorkerGameState.CurrentVessel == null) ? 0 : KACWorkerGameState.CurrentVessel.orbit.timeToAp;
                        WindowLayout_AddPane_NodeEvent(KACWorkerGameState.ApPointExists && !KACWorkerGameState.CurrentVessel.LandedOrSplashed, dblTimeToPoint);
                    }
                    break;
                case KACAlarm.AlarmTypeEnum.Periapsis:
                    WindowLayout_AddTypeApPe();
                    if (!KACWorkerGameState.PePointExists && HighLogic.CurrentGame.Mode == Game.Modes.CAREER &&
                        GameVariables.Instance.GetOrbitDisplayMode(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation)) != GameVariables.OrbitDisplayMode.PatchedConics)
                    {
                        GUILayout.Label("Periapsis Alarms cannot be set without upgrading the Tracking Station", GUILayout.ExpandWidth(true));
                    }
                    else
                    {
                    dblTimeToPoint = (KACWorkerGameState.CurrentVessel == null ) ? 0 : KACWorkerGameState.CurrentVessel.orbit.timeToPe;
                    WindowLayout_AddPane_NodeEvent(KACWorkerGameState.PePointExists && !KACWorkerGameState.CurrentVessel.LandedOrSplashed, dblTimeToPoint);
                    }
                    break;
                case KACAlarm.AlarmTypeEnum.AscendingNode:
                    WindowLayout_AddTypeANDN();
                    if (KACWorkerGameState.CurrentVesselTarget==null)
                    {
                        WindowLayout_AddPane_AscendingNodeEquatorial();
                    }
                    else if (KACWorkerGameState.CurrentVessel.orbit.referenceBody == KACWorkerGameState.CurrentVesselTarget.GetOrbit().referenceBody) {
                        //Must be orbiting Same parent body for this to make sense
                        WindowLayout_AddPane_AscendingNode();
                    }
                    else
                    {
                        GUILayout.Label("Target Not Currently Orbiting Same Parent", KACResources.styleAddXferName, GUILayout.Height(18));
                        GUILayout.Label("", KACResources.styleAddXferName, GUILayout.Height(18));
                        GUILayout.Label("There is no Ascending Node between orbits", KACResources.styleAddXferName, GUILayout.Height(18));
                    }
                    break;
                case KACAlarm.AlarmTypeEnum.DescendingNode:
                    WindowLayout_AddTypeANDN();
                    if (KACWorkerGameState.CurrentVesselTarget==null)
                    {
                        WindowLayout_AddPane_DescendingNodeEquatorial();
                    }
                    else if (KACWorkerGameState.CurrentVessel.orbit.referenceBody == KACWorkerGameState.CurrentVesselTarget.GetOrbit().referenceBody)
                    {
                        //Must be orbiting Same parent body for this to make sense
                        WindowLayout_AddPane_DescendingNode();
                    }
                    else
                    {
                        GUILayout.Label("Target Not Currently Orbiting Same Parent", KACResources.styleAddXferName, GUILayout.Height(18));
                        GUILayout.Label("", KACResources.styleAddXferName, GUILayout.Height(18));
                        GUILayout.Label("There is no Ascending Node between orbits", KACResources.styleAddXferName, GUILayout.Height(18));
                    }
                    break;
                case KACAlarm.AlarmTypeEnum.LaunchRendevous:
                    WindowLayout_AddTypeANDN();
                    if (KACWorkerGameState.CurrentVessel.orbit.referenceBody == KACWorkerGameState.CurrentVesselTarget.GetOrbit().referenceBody)
                    {
                        //Must be orbiting Same parent body for this to make sense
                        WindowLayout_AddPane_LaunchRendevous();
                    }
                    else
                    {
                        GUILayout.Label("Target Not Currently Orbiting Same Parent", KACResources.styleAddXferName, GUILayout.Height(18));
                        GUILayout.Label("", KACResources.styleAddXferName, GUILayout.Height(18));
                        GUILayout.Label("There is no Ascending Node between orbits", KACResources.styleAddXferName, GUILayout.Height(18));
                    }

                    break;
                case KACAlarm.AlarmTypeEnum.Closest:
                    WindowLayout_AddTypeDistanceChoice();
                    WindowLayout_AddPane_ClosestApproach();
                    break;
                case KACAlarm.AlarmTypeEnum.Distance:
                    WindowLayout_AddTypeDistanceChoice();
                    WindowLayout_AddPane_TargetDistance();
                    break;
                case KACAlarm.AlarmTypeEnum.Crew:
                    WindowLayout_AddPane_Crew();
                    break;
                case KACAlarm.AlarmTypeEnum.Contract:
                case KACAlarm.AlarmTypeEnum.ContractAuto:
                    WindowLayout_AddPane_Contract();
                    break;
                default:
                    break;
            }

            GUILayout.EndVertical();
            
            SetTooltipText();
        }

        internal void WindowLayout_AddTypeApPe()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("apsis Type:", KACResources.styleAddHeading);
            int intOption = 0;
            if (AddType != KACAlarm.AlarmTypeEnum.Apoapsis) intOption = 1;
            if (DrawRadioList(ref intOption, "Apoapsis", "Periapsis"))
            {
                if (intOption == 0)
                    AddType = KACAlarm.AlarmTypeEnum.Apoapsis;
                else
                {
                    AddType = KACAlarm.AlarmTypeEnum.Periapsis;
                }
                AddTypeChanged();

            }

            GUILayout.EndHorizontal();
        }


        ////Variabled for Raw Alarm screen
        //String strYears = "0", strDays = "0", strHours = "0", strMinutes = "0",
        private String strRawUT="0";
        private KSPDateTime rawTime = new KSPDateTime(600);
        private KSPTimeSpan rawTimeToAlarm = new KSPTimeSpan(0);
        //Boolean blnRawDate = false;
        //Boolean blnRawInterval = true;
        ///// <summary>
        ///// Layout the raw alarm screen inputs
        ///// </summary>
        private Int32 intRawType = 1;
        internal KACTimeStringArray rawEntry = new KACTimeStringArray(600, KACTimeStringArray.TimeEntryPrecisionEnum.Years);
        private void WindowLayout_AddPane_Raw()
        {
            GUILayout.Label("Enter Raw Time Values...", KACResources.styleAddSectionHeading);

            GUILayout.BeginVertical(KACResources.styleAddFieldAreas);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Time type:", KACResources.styleAddHeading, GUILayout.Width(90));
            if (DrawRadioList(ref intRawType, new string[] { "Date", "Time Interval" }))
            {
                if (intRawType == 0)
                {
                    rawEntry = new KACTimeStringArray(Planetarium.GetUniversalTime() + 600, KACTimeStringArray.TimeEntryPrecisionEnum.Years);
                }
            }
            GUILayout.EndHorizontal();

            if (intRawType == 0)
            {
                //date
                KACTimeStringArray rawDate = new KACTimeStringArray(rawEntry.UT + KSPDateStructure.EpochAsKSPDateTime.UT, KACTimeStringArray.TimeEntryPrecisionEnum.Years);
                if (DrawTimeEntry(ref rawDate, KACTimeStringArray.TimeEntryPrecisionEnum.Years, "Time:", 50, 35, 15))
                {
                    rawEntry.BuildFromUT(rawDate.UT - KSPDateStructure.EpochAsKSPDateTime.UT);
                }
            }
            else
            {
                //interval
                if (DrawTimeEntry(ref rawEntry, KACTimeStringArray.TimeEntryPrecisionEnum.Years, "Time:", 50, 35, 15))
                {

                }
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("UT (raw seconds):", KACResources.styleAddHeading,GUILayout.Width(100));
            strRawUT = GUILayout.TextField(strRawUT, KACResources.styleAddField);
            GUILayout.EndHorizontal();


            GUILayout.EndVertical();
            try
            {
                if (strRawUT != "")
                    rawTime.UT = Convert.ToDouble(strRawUT);
                else
                    rawTime.UT = rawEntry.UT;
        
                //If its an interval add the interval to the current time
                if (intRawType==1)
                    rawTime = new KSPDateTime(KACWorkerGameState.CurrentTime.UT + rawTime.UT);

                rawTimeToAlarm = new KSPTimeSpan(rawTime.UT - KACWorkerGameState.CurrentTime.UT);

                //Draw the Add Alarm details at the bottom
                if (DrawAddAlarm(rawTime,null,rawTimeToAlarm))
                {
                    //"VesselID, Name, Message, AlarmTime.UT, Type, Enabled,  HaltWarp, PauseGame, Maneuver"
                    String strVesselID = "";
                    if (KACWorkerGameState.CurrentVessel != null && blnAlarmAttachToVessel) strVesselID = KACWorkerGameState.CurrentVessel.id.ToString();
                    KACAlarm alarmNew = new KACAlarm(strVesselID, strAlarmName, (blnRepeatingXferAlarm ? "Alarm Repeats\r\n" : "") + strAlarmNotes, rawTime.UT, 0, KACAlarm.AlarmTypeEnum.Raw,
                        AddAction);
                    alarmNew.RepeatAlarm = blnRepeatingXferAlarm;
                    alarmNew.RepeatAlarmPeriod = new KSPTimeSpan(timeRepeatPeriod.UT);
                    alarms.Add(alarmNew);

                    //settings.Save();
                    _ShowAddPane = false;
                }
            }
            catch (Exception ex)
            {
                GUILayout.Label("Unable to combine all text fields to date", GUILayout.ExpandWidth(true));
                LogFormatted_DebugOnly("{0}\r\n{1}", ex.Message, ex.StackTrace);
            }
        }

        private int intSelectedCrew = 0;
        //Do we do this in with the Raw alarm as we are gonna ask for almost the same stuff
        private String strCrewUT = "0";
        private KSPDateTime CrewTime = new KSPDateTime(600);
        private KSPTimeSpan CrewTimeToAlarm = new KSPTimeSpan(0);
        private Int32 intCrewType = 1;
        private KACTimeStringArray CrewEntry = new KACTimeStringArray(600, KACTimeStringArray.TimeEntryPrecisionEnum.Years);
        private Boolean CrewAlarmStoreNode = false;
        private  Int32 intAddCrewHeight = 322;
        private void WindowLayout_AddPane_Crew()
        {
            intAddCrewHeight = 304;// 322;
            GUILayout.Label("Select Crew...", KACResources.styleAddSectionHeading);
            if (KACWorkerGameState.CurrentVessel == null)
            {
                GUILayout.Label("No Active Vessel");
            }
            else
            {
                GUILayout.BeginVertical(KACResources.styleAddFieldAreas);

                //get the kerbals in the current vessel
                List<ProtoCrewMember> pCM = KACWorkerGameState.CurrentVessel.GetVesselCrew();
                intAddCrewHeight += (pCM.Count * 30);
                if (pCM.Count == 0)
                {
                    //Draw something about no crew present
                    GUILayout.Label("No Kerbals present in this vessel", KACResources.styleContent, GUILayout.ExpandWidth(true));
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Kerbal Name", KACResources.styleAddSectionHeading, GUILayout.Width(267));
                    GUILayout.Label("Add", KACResources.styleAddSectionHeading);//, GUILayout.Width(30));
                    GUILayout.EndHorizontal();

                    for (int intTarget = 0; intTarget < pCM.Count; intTarget++)
                    {
                        //LogFormatted("{2}", pCM[intTarget].name);
                        GUILayout.BeginHorizontal();
                        //        //draw a line and a radio button for selecting Crew
                        GUILayout.Space(20);
                        GUILayout.Label(pCM[intTarget].name, KACResources.styleAddXferName, GUILayout.Width(240), GUILayout.Height(20));

                        //        //when they are selected adjust message to have a name of the crew member, and message of vessel when alarm was set
                        Boolean blnSelected = (intSelectedCrew == intTarget);
                        if (DrawToggle(ref blnSelected, "", KACResources.styleCheckbox, GUILayout.Width(40)))
                        {
                            if (blnSelected)
                            {
                                intSelectedCrew = intTarget;
                                BuildCrewStrings();
                            }
                        }
                        GUILayout.EndHorizontal();
                    }

                    DrawCheckbox(ref CrewAlarmStoreNode, "Store Man Node/Target with Crew Alarm");

                }
                GUILayout.EndVertical();

                if (pCM.Count > 0)
                {
                    //Now the time entry area
                    GUILayout.Label("Enter Time Values...", KACResources.styleAddSectionHeading);

                    GUILayout.BeginVertical(KACResources.styleAddFieldAreas);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Time type:", KACResources.styleAddHeading, GUILayout.Width(90));
                    if (DrawRadioList(ref intCrewType, new string[] { "Date", "Time Interval" }))
                    {
                        if (intRawType == 0)
                        {
                            rawEntry = new KACTimeStringArray(Planetarium.GetUniversalTime() + 600, KACTimeStringArray.TimeEntryPrecisionEnum.Years);
                        }
                    }
                    GUILayout.EndHorizontal();

                    if (intCrewType == 0)
                    {
                        //date
                        KACTimeStringArray CrewDate = new KACTimeStringArray(CrewEntry.UT + KSPDateStructure.EpochAsKSPDateTime.UT, KACTimeStringArray.TimeEntryPrecisionEnum.Years);
                        if (DrawTimeEntry(ref CrewDate, KACTimeStringArray.TimeEntryPrecisionEnum.Years, "Time:", 50, 35, 15))
                        {
                            rawEntry.BuildFromUT(CrewDate.UT - KSPDateStructure.EpochAsKSPDateTime.UT);
                        }
                    }
                    else
                    {
                        //interval
                        if (DrawTimeEntry(ref CrewEntry, KACTimeStringArray.TimeEntryPrecisionEnum.Years, "Time:", 50, 35, 15))
                        {

                        }
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("UT (raw seconds):", KACResources.styleAddHeading, GUILayout.Width(100));
                    strCrewUT = GUILayout.TextField(strCrewUT, KACResources.styleAddField);
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();

                    try
                    {
                        if (strCrewUT != "")
                            CrewTime.UT = Convert.ToDouble(strCrewUT);
                        else
                            CrewTime.UT = CrewEntry.UT;

                        //If its an interval add the interval to the current time
                        if (intCrewType == 1)
                            CrewTime = new KSPDateTime(KACWorkerGameState.CurrentTime.UT + CrewTime.UT);

                        CrewTimeToAlarm = new KSPTimeSpan(CrewTime.UT - KACWorkerGameState.CurrentTime.UT);

                        //Draw the Add Alarm details at the bottom
                        if (DrawAddAlarm(CrewTime, null, CrewTimeToAlarm))
                        {
                            //"VesselID, Name, Message, AlarmTime.UT, Type, Enabled,  HaltWarp, PauseGame, Maneuver"
                            KACAlarm addAlarm = new KACAlarm(pCM[intSelectedCrew].name, strAlarmName, (blnRepeatingXferAlarm ? "Alarm Repeats\r\n" : "") + strAlarmNotes, CrewTime.UT, 0, KACAlarm.AlarmTypeEnum.Crew,
                                AddAction);
                            if (CrewAlarmStoreNode)
                            {
                                if (KACWorkerGameState.ManeuverNodeExists) addAlarm.ManNodes = KACWorkerGameState.ManeuverNodesFuture;
                                if (KACWorkerGameState.CurrentVesselTarget != null) addAlarm.TargetObject = KACWorkerGameState.CurrentVesselTarget;
                            }
                            addAlarm.RepeatAlarm = blnRepeatingXferAlarm;
                            addAlarm.RepeatAlarmPeriod = new KSPTimeSpan(timeRepeatPeriod.UT);

                            alarms.Add(addAlarm);
                            //settings.Save();
                            _ShowAddPane = false;
                        }
                    }
                    catch (Exception)
                    {
                        //    LogFormatted(ex.Message);
                        GUILayout.Label("Unable to combine all text fields to date", GUILayout.ExpandWidth(true));
                    }
                }
            }
        }

        Vector2 scrollContract = new Vector2(0, 0);
        Int32 intSelectedContract = 0;
        private KSPDateTime ContractTime = new KSPDateTime(600);
        private KSPTimeSpan ContractTimeToEvent = new KSPTimeSpan(0);
        private KSPTimeSpan ContractTimeToAlarm = new KSPTimeSpan(0);

        internal List<Contract> lstContracts;
        internal Contract.State contractLastState = Contract.State.Offered;
        
        private void WindowLayout_AddPane_Contract()
        {
            GUILayout.Label("Select Contract...", KACResources.styleAddSectionHeading);
            if (Contracts.ContractSystem.Instance == null)
            {
                GUILayout.BeginVertical(KACResources.styleAddFieldAreas);
                GUILayout.Label("Jebediah digs around in the Sandbox, but can't find any contracts in this game mode", KACResources.styleContent);
                GUILayout.EndVertical();
            }
            else
            {

                if (lstContracts.Count == 0)
                {
                    GUILayout.BeginVertical(KACResources.styleAddFieldAreas);
                    GUILayout.Label("No Contracts Offered or Active", KACResources.styleContent);
                    GUILayout.EndVertical();
                }
                else
                {

                    scrollContract = GUILayout.BeginScrollView(scrollContract, KACResources.styleAddFieldAreas);

                    //If the selected contract is already an alarm then move the selected one
                    if (intSelectedContract == -1  || alarms.Any(a => a.ContractGUID == lstContracts[intSelectedContract].ContractGuid)){
                        intSelectedContract = -1;
                        for (int i = 0; i < lstContracts.Count; i++) {
                            if(!alarms.Any(a => a.ContractGUID == lstContracts[i].ContractGuid)){
                                intSelectedContract = i;
                                BuildContractStringsAndMargin();
                                break;
                            }
                        }
                    }
                    
                    //Loop through the contracts to draw the lines
                    for (Int32 intTarget = 0; intTarget < lstContracts.Count; intTarget++)
                    {
                        Contract c = lstContracts[intTarget];
                        Boolean AlarmExists = alarms.Any(a => a.ContractGUID == c.ContractGuid);

                        GUILayout.BeginHorizontal();
                        //Appropriate icon
                        GUILayout.Space(5);
                        if (AlarmExists) {
                            GUILayout.Label(new GUIContent(KACResources.iconRaw, "Alarm Exists"), GUILayout.Width(20), GUILayout.Height(25));
                        } else if (c.ContractState == Contract.State.Active) {
                            GUILayout.Label(new GUIContent(KACResources.iconContract, "Active"), GUILayout.Width(20), GUILayout.Height(25));
                        } else {
                            GUILayout.Space(24);
                        }

                        //What style should the name be
                        GUIStyle styleContLabel = KACResources.styleContractLabelOffer;
                        if (c.ContractState == Contract.State.Active)
                            styleContLabel = KACResources.styleContractLabelActive;
                        else if (AlarmExists)
                            styleContLabel = KACResources.styleContractLabelAlarmExists;

                        if (GUILayout.Button(c.Title, styleContLabel, GUILayout.Width(243)))
                        {
                            if (!AlarmExists)
                            {
                                intSelectedContract = intTarget;
                                BuildContractStringsAndMargin();
                            }
                        };

                        //Is the check box on?
                        Boolean blnSelected = (intSelectedContract == intTarget);

                        if (!AlarmExists)
                        {
                            if (DrawToggle(ref blnSelected, "", KACResources.styleCheckbox, GUILayout.Width(20)))
                            {
                                if (blnSelected)
                                {
                                    intSelectedContract = intTarget;
                                    BuildContractStringsAndMargin();
                                }
                            }
                        } GUILayout.EndHorizontal();
                    }
                    GUILayout.EndScrollView();

                    if (intSelectedContract < 0) {
                        GUILayout.Label("All Contracts Have alarms set already", KACResources.styleContent);
                    } else { 
                        //Draw the Add Alarm details at the bottom
                        //If its an interval add the interval to the current time
                        ContractTime = new KSPDateTime(lstContracts[intSelectedContract].DateNext());
                        ContractTimeToEvent = new KSPTimeSpan(lstContracts[intSelectedContract].DateNext() - KACWorkerGameState.CurrentTime.UT);
                        ContractTimeToAlarm = new KSPTimeSpan(ContractTimeToEvent.UT - timeMargin.UT);

                        if (DrawAddAlarm(ContractTime, ContractTimeToEvent, ContractTimeToAlarm))
                        {
                            //"VesselID, Name, Message, AlarmTime.UT, Type, Enabled,  HaltWarp, PauseGame, Maneuver"
                            String strVesselID = "";
                            if (KACWorkerGameState.CurrentVessel != null && blnAlarmAttachToVessel) strVesselID = KACWorkerGameState.CurrentVessel.id.ToString();
                            KACAlarm tmpAlarm = new KACAlarm(strVesselID, strAlarmName, strAlarmNotes, KACWorkerGameState.CurrentTime.UT + ContractTimeToAlarm.UT,
                                timeMargin.UT, KACAlarm.AlarmTypeEnum.Contract, AddAction);

                            tmpAlarm.ContractGUID = lstContracts[intSelectedContract].ContractGuid;
                            tmpAlarm.ContractAlarmType = lstContracts[intSelectedContract].AlarmType();

                            alarms.Add(tmpAlarm);
                            //settings.Save();
                            _ShowAddPane = false;
                        }
                    }
                }
            }
            
        }

        private Boolean DrawAddAlarm(KSPDateTime AlarmDate, KSPTimeSpan TimeToEvent, KSPTimeSpan TimeToAlarm, Boolean ForceShowRepeat = false)
        {
            Boolean blnReturn = false;
            intHeight_AddWindowRepeat = 0;
            int intLineHeight = 18;

            GUILayout.BeginVertical();

            //Do we show repeating options
            if (KACAlarm.AlarmTypeSupportsRepeat.Contains(AddType) || ForceShowRepeat)
            {
                intHeight_AddWindowRepeat += 53;
                GUILayout.Label("Alarm Repeat Options...", KACResources.styleAddSectionHeading);
                GUILayout.BeginVertical(KACResources.styleAddFieldAreas);
                DrawCheckbox(ref blnRepeatingXferAlarm, new GUIContent("Make this alarm repeat when triggered", "If enabled then when one alarm fires another will be created based on the existing alarm"));
                if (KACAlarm.AlarmTypeSupportsRepeatPeriod.Contains(AddType))
                {
                    intHeight_AddWindowRepeat += 24;
                    DrawTimeEntry(ref timeRepeatPeriod, KACTimeStringArray.TimeEntryPrecisionEnum.Days, "Repeat After:", 90);
                }
                GUILayout.EndVertical();
            }
        
            //Now for the add area
            GUILayout.BeginHorizontal(KACResources.styleAddAlarmArea);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Date:", KACResources.styleAddHeading, GUILayout.Height(intLineHeight), GUILayout.Width(40), GUILayout.MaxWidth(40));
            GUILayout.Label(AlarmDate.ToStringStandard(DateStringFormatsEnum.DateTimeFormat), KACResources.styleContent, GUILayout.Height(intLineHeight));
            GUILayout.EndHorizontal();
            if (TimeToEvent != null)
            {
                GUILayout.BeginHorizontal();
                //GUILayout.Label("Time to " + strAlarmEventName + ":", KACResources.styleAddHeading, GUILayout.Height(intLineHeight), GUILayout.Width(120), GUILayout.MaxWidth(120));
                GUILayout.Label("Time to " + strAlarmEventName + ":", KACResources.styleAddHeading, GUILayout.Height(intLineHeight));
                GUILayout.Label(TimeToEvent.ToStringStandard(settings.TimeSpanFormat), KACResources.styleContent, GUILayout.Height(intLineHeight));
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            //GUILayout.Label("Time to Alarm:", KACResources.styleAddHeading, GUILayout.Height(intLineHeight), GUILayout.Width(120), GUILayout.MaxWidth(120));
            GUILayout.Label("Time to Alarm:", KACResources.styleAddHeading, GUILayout.Height(intLineHeight));
            GUILayout.Label(TimeToAlarm.ToStringStandard(settings.TimeSpanFormat), KACResources.styleContent, GUILayout.Height(intLineHeight));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(10);
            int intButtonHeight = 36;
            if (TimeToEvent != null) intButtonHeight += 22;
            if (GUILayout.Button("Add Alarm", KACResources.styleButton, GUILayout.Width(75), GUILayout.Height(intButtonHeight)))
            {
                blnReturn = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            return blnReturn;
        }

        ////Variables for Node Alarms screen
        ////String strNodeMargin = "1";
        ///// <summary>
        ///// Screen Layout for adding Alarm from Maneuver Node
        ///// </summary>
        private void WindowLayout_AddPane_Maneuver()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Node Details...", KACResources.styleAddSectionHeading);

            if (KACWorkerGameState.CurrentVessel == null)
            {
                GUILayout.Label("No Active Vessel");
            }
            else
            {
                if (!KACWorkerGameState.ManeuverNodeExists)
                {
                    GUILayout.Label("No Maneuver Nodes Found", GUILayout.ExpandWidth(true));
                }
                else
                {
                    Boolean blnFoundNode = false;
                    String strMarginConversion = "";
                    //loop to find the first future node
                    for (int intNode = 0; (intNode < KACWorkerGameState.CurrentVessel.patchedConicSolver.maneuverNodes.Count) && !blnFoundNode; intNode++)
                    {
                        KSPDateTime nodeTime = new KSPDateTime(KACWorkerGameState.CurrentVessel.patchedConicSolver.maneuverNodes[intNode].UT);
                        KSPTimeSpan nodeInterval = new KSPTimeSpan(nodeTime.UT - KACWorkerGameState.CurrentTime.UT);

                        KSPDateTime nodeAlarm;
                        KSPTimeSpan nodeAlarmInterval;
                        try
                        {
                            nodeAlarm = new KSPDateTime(nodeTime.UT - timeMargin.UT);
                            nodeAlarmInterval = new KSPTimeSpan(nodeTime.UT - KACWorkerGameState.CurrentTime.UT - timeMargin.UT);
                        }
                        catch (Exception)
                        {
                            nodeAlarm = null;
                            nodeAlarmInterval = null;
                            strMarginConversion = "Unable to Add the Margin Minutes";
                        }

                        if ((nodeTime.UT > KACWorkerGameState.CurrentTime.UT) && strMarginConversion == "")
                        {
                            if (DrawAddAlarm(nodeTime,nodeInterval,nodeAlarmInterval))
                            {
                                //Get a list of all future Maneuver Nodes - thats what the skip does
                                List<ManeuverNode> manNodesToStore = KACWorkerGameState.CurrentVessel.patchedConicSolver.maneuverNodes.Skip(intNode).ToList<ManeuverNode>();

                                alarms.Add(new KACAlarm(KACWorkerGameState.CurrentVessel.id.ToString(), strAlarmName, strAlarmNotes, nodeAlarm.UT, timeMargin.UT, KACAlarm.AlarmTypeEnum.Maneuver,
                                    AddAction, manNodesToStore));
                                //settings.Save();
                                _ShowAddPane = false;
                            }
                            blnFoundNode = true;
                        }
                    }

                    if (strMarginConversion != "")
                        GUILayout.Label(strMarginConversion, GUILayout.ExpandWidth(true));
                    else if (!blnFoundNode)
                        GUILayout.Label("No Future Maneuver Nodes Found", GUILayout.ExpandWidth(true));
                }
            }

            GUILayout.EndVertical();
        }


        private List<KACAlarm.AlarmTypeEnum> lstAlarmsWithTarget = new List<KACAlarm.AlarmTypeEnum> { KACAlarm.AlarmTypeEnum.AscendingNode, KACAlarm.AlarmTypeEnum.DescendingNode, KACAlarm.AlarmTypeEnum.LaunchRendevous };
        private void WindowLayout_AddPane_NodeEvent(Boolean PointFound,Double timeToPoint)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(strAlarmEventName + " Details...", KACResources.styleAddSectionHeading);
            if (lstAlarmsWithTarget.Contains(AddType))
            {
                if (KACWorkerGameState.CurrentVesselTarget == null)
                    GUILayout.Label("Equatorial Nodes (No Valid Target)", KACResources.styleAddXferName, GUILayout.Height(18));
                else
                {
                    if (KACWorkerGameState.CurrentVesselTarget is Vessel)
                        GUILayout.Label("Target Vessel: " + KACWorkerGameState.CurrentVesselTarget.GetVessel().vesselName, KACResources.styleAddXferName,GUILayout.Height(18));
                    else if (KACWorkerGameState.CurrentVesselTarget is CelestialBody)
                        GUILayout.Label("Target Body: " + ((CelestialBody)KACWorkerGameState.CurrentVesselTarget).bodyName, KACResources.styleAddXferName,GUILayout.Height(18));
                    else
                        GUILayout.Label("Object Targeted", KACResources.styleAddXferName, GUILayout.Height(18));
                        //GUILayout.Label("Target Vessel: " + KACWorkerGameState.CurrentVesselTarget.GetVessel().vesselName, KACResources.styleAddXferName, GUILayout.Height(18));
                }
            }

            if (KACWorkerGameState.CurrentVessel == null)
                GUILayout.Label("No Active Vessel");
            else
            {
                if (!PointFound)
                {
                    GUILayout.Label("No " + strAlarmEventName + " Point Found on current plan", GUILayout.ExpandWidth(true));
                }
                else
                {
                    String strMarginConversion = "";
                    KSPDateTime eventTime = new KSPDateTime(KACWorkerGameState.CurrentTime.UT + timeToPoint);
                    KSPTimeSpan eventInterval = new KSPTimeSpan(timeToPoint);

                    KSPDateTime eventAlarm;
                    KSPTimeSpan eventAlarmInterval;
                    try
                    {
                        eventAlarm = new KSPDateTime(eventTime.UT - timeMargin.UT);
                        eventAlarmInterval = new KSPTimeSpan(eventTime.UT - KACWorkerGameState.CurrentTime.UT - timeMargin.UT);
                    }
                    catch (Exception)
                    {
                        eventAlarm = null;
                        eventAlarmInterval = null;
                        strMarginConversion = "Unable to Add the Margin Minutes";
                    }

                    if ((eventTime.UT > KACWorkerGameState.CurrentTime.UT) && strMarginConversion == "")
                    {
                        if (DrawAddAlarm(eventTime, eventInterval, eventAlarmInterval))
                        {
                            KACAlarm newAlarm = new KACAlarm(KACWorkerGameState.CurrentVessel.id.ToString(), strAlarmName, strAlarmNotes, eventAlarm.UT, timeMargin.UT, AddType,
                                AddAction);
                            if (lstAlarmsWithTarget.Contains(AddType))
                                newAlarm.TargetObject = KACWorkerGameState.CurrentVesselTarget;

                            alarms.Add(newAlarm);
                            //settings.Save();
                            _ShowAddPane = false;
                        }
                    }
                    else
                    {
                        strMarginConversion = "No Future " + strAlarmEventName + "Points found";
                    }

                    if (strMarginConversion != "")
                        GUILayout.Label(strMarginConversion, GUILayout.ExpandWidth(true));
                }
            }

            GUILayout.EndVertical();
        }

        private List<CelestialBody> XferParentBodies = new List<CelestialBody>();
        private List<CelestialBody> XferOriginBodies = new List<CelestialBody>();
        private List<KACXFerTarget> XferTargetBodies = new List<KACXFerTarget>();

        private static int SortByDistance(CelestialBody c1, CelestialBody c2)
        {
            Double f1 = c1.orbit.semiMajorAxis;
            double f2 = c2.orbit.semiMajorAxis;
            //LogFormatted("{0}-{1}", f1.ToString(), f2.ToString());
            return f1.CompareTo(f2);
        }


        private int intXferCurrentParent = 0;
        private int intXferCurrentOrigin = 0;
        private int intXferCurrentTarget = 0;
        //private KerbalTime XferCurrentTargetEventTime;
        private Boolean blnRepeatingXferAlarm = false;

        private void SetUpXferParents()
        {
            XferParentBodies = new List<CelestialBody>();
            //Build a list of parents - Cant sort this normally as the Sun has no radius - duh!
            foreach (CelestialBody tmpBody in FlightGlobals.Bodies)
            {
                //add any body that has more than 1 child to the parents list
                if (tmpBody.orbitingBodies.Count > 1)
                    XferParentBodies.Add(tmpBody);
            }
        }

        private void SetupXferOrigins()
        {
            //set the possible origins to be all the orbiting bodies around the parent
            XferOriginBodies = new List<CelestialBody>();
            XferOriginBodies = XferParentBodies[intXferCurrentParent].orbitingBodies.OrderBy(b => b.orbit.semiMajorAxis).ToList<CelestialBody>();
            if (intXferCurrentOrigin > XferOriginBodies.Count)
                intXferCurrentOrigin = 0;

            if (AddType== KACAlarm.AlarmTypeEnum.Transfer || AddType== KACAlarm.AlarmTypeEnum.TransferModelled) BuildTransferStrings();
        }

        private void SetupXFerTargets()
        {
            XferTargetBodies = new List<KACXFerTarget>();

            //Loop through the Siblings of the origin planet
            foreach (CelestialBody bdyTarget in XferOriginBodies.OrderBy(b => b.orbit.semiMajorAxis))
            {
                //add all the other siblings as target possibilities
                if (bdyTarget != XferOriginBodies[intXferCurrentOrigin])
                {
                    KACXFerTarget tmpTarget = new KACXFerTarget();
                    tmpTarget.Origin = XferOriginBodies[intXferCurrentOrigin];
                    tmpTarget.Target = bdyTarget;
                    //tmpTarget.SetPhaseAngleTarget();
                    //add it to the list
                    XferTargetBodies.Add(tmpTarget);
                }
            }
            if (intXferCurrentTarget > XferTargetBodies.Count)
                intXferCurrentTarget = 0;

            if (AddType == KACAlarm.AlarmTypeEnum.Transfer || AddType == KACAlarm.AlarmTypeEnum.TransferModelled) BuildTransferStrings();
        }

        private int intAddXferHeight = 317;
        private int intXferType = 1;
        private void WindowLayout_AddPane_Transfer()
        {
            intAddXferHeight = 304;// 317;

            if (settings.RSSActive) {
                GUILayout.Label("RSS detected - it is recommended that you use the Transfer Window Planner Plugin to plan transfers", KACResources.styleAddXferName);
                GUILayout.Space(-8);
                if (GUILayout.Button("Click here to open the TWP forum thread", KACResources.styleContent))
                    Application.OpenURL("http://forum.kerbalspaceprogram.com/threads/93115");
                intAddXferHeight += 58;
            }

            KSPDateTime XferCurrentTargetEventTime = null;
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Transfers", KACResources.styleAddSectionHeading, GUILayout.Width(60));
            //add something here to select the modelled or formula values for Solar orbiting bodies
            if (settings.XferModelDataLoaded)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Calc by:", KACResources.styleAddHeading);
                if (intXferCurrentParent == 0)
                {
                    //intAddXferHeight += 35;
                    if (DrawRadioList(ref intXferType, "Model", "Formula"))
                    {
                        settings.XferUseModelData = (intXferType == 0);
                        settings.Save();
                    }
                }
                else
                {
                    int zero = 0;
                    DrawRadioList(ref zero, "Formula");
                }
            }
            GUILayout.EndHorizontal();
            try
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Xfer Parent:", KACResources.styleAddHeading, GUILayout.Width(80), GUILayout.Height(20));
                GUILayout.Label(XferParentBodies[intXferCurrentParent].bodyName, KACResources.styleAddXferName, GUILayout.ExpandWidth(true), GUILayout.Height(20));
                if (GUILayout.Button(new GUIContent("Change", "Click to cycle through Parent Bodies"), KACResources.styleAddXferOriginButton))
                {
                    intXferCurrentParent += 1;
                    if (intXferCurrentParent >= XferParentBodies.Count) intXferCurrentParent = 0;
                    SetupXferOrigins();
                    intXferCurrentOrigin = 0;
                    SetupXFerTargets();
                    BuildTransferStrings();
                    //strAlarmNotesNew = String.Format("{0} Transfer", XferOriginBodies[intXferCurrentOrigin].bodyName);
                }
                GUILayout.Space(34);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Xfer Origin:", KACResources.styleAddHeading, GUILayout.Width(80),GUILayout.Height(20));
                GUILayout.Label(XferOriginBodies[intXferCurrentOrigin].bodyName, KACResources.styleAddXferName, GUILayout.ExpandWidth(true), GUILayout.Height(20));
                if (GUILayout.Button(new GUIContent("Change", "Click to cycle through Origin Bodies"), KACResources.styleAddXferOriginButton))
                {
                    intXferCurrentOrigin += 1;
                    if (intXferCurrentOrigin >= XferOriginBodies.Count) intXferCurrentOrigin = 0;
                    SetupXFerTargets();
                    BuildTransferStrings();
                    //strAlarmNotesNew = String.Format("{0} Transfer", XferOriginBodies[intXferCurrentOrigin].bodyName);
                }

                if (!settings.AlarmXferDisplayList)
                    GUILayout.Space(34);
                else
                    if (GUILayout.Button(new GUIContent(KACResources.btnChevronUp, "Hide Full List"), KACResources.styleSmallButton))
                    {
                        settings.AlarmXferDisplayList = !settings.AlarmXferDisplayList;
                        settings.Save();
                    }
                GUILayout.EndHorizontal();

                if (!settings.AlarmXferDisplayList)
                {
                    //Simple single chosen target
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Xfer Target:", KACResources.styleAddHeading, GUILayout.Width(80), GUILayout.Height(20));
                    GUILayout.Label(XferTargetBodies[intXferCurrentTarget].Target.bodyName, KACResources.styleAddXferName, GUILayout.ExpandWidth(true), GUILayout.Height(20));
                    if (GUILayout.Button(new GUIContent("Change", "Click to cycle through Target Bodies"), KACResources.styleAddXferOriginButton))
                    {
                        intXferCurrentTarget += 1;
                        if (intXferCurrentTarget >= XferTargetBodies.Count) intXferCurrentTarget = 0;
                        SetupXFerTargets();
                        BuildTransferStrings();
                        //strAlarmNotesNew = String.Format("{0} Transfer", XferTargetBodies[intXferCurrentTarget].Target.bodyName);
                    }
                    if (GUILayout.Button(new GUIContent(KACResources.btnChevronDown, "Show Full List"), KACResources.styleSmallButton))
                    {
                        settings.AlarmXferDisplayList = !settings.AlarmXferDisplayList;
                        settings.Save();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal(); 
                    GUILayout.Label("Phase Angle-Current:",  KACResources.styleAddHeading,GUILayout.Width(130));
                    GUILayout.Label(String.Format("{0:0.00}", XferTargetBodies[intXferCurrentTarget].PhaseAngleCurrent), KACResources.styleContent, GUILayout.Width(67));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Phase Angle-Target:", KACResources.styleAddHeading, GUILayout.Width(130));
                    if (intXferCurrentParent != 0 || (!settings.XferUseModelData && settings.XferModelDataLoaded))
                    {
                        //formula based
                        GUILayout.Label(String.Format("{0:0.00}", XferTargetBodies[intXferCurrentTarget].PhaseAngleTarget), KACResources.styleContent, GUILayout.Width(67));
                    }
                    else
                    {
                        //this is the modelled data, but only for Kerbol orbiting bodies
                        try
                        {
                            KACXFerModelPoint tmpModelPoint = KACResources.lstXferModelPoints.FirstOrDefault(
                            m => FlightGlobals.Bodies[m.Origin] == XferTargetBodies[intXferCurrentTarget].Origin &&
                                FlightGlobals.Bodies[m.Target] == XferTargetBodies[intXferCurrentTarget].Target &&
                                m.UT >= KACWorkerGameState.CurrentTime.UT);

                            if (tmpModelPoint != null)
                            {
                                GUILayout.Label(String.Format("{0:0.00}", tmpModelPoint.PhaseAngle), KACResources.styleContent, GUILayout.Width(67));
                                XferCurrentTargetEventTime = new KSPDateTime(tmpModelPoint.UT);
                            }
                            else
                            {
                                GUILayout.Label("No future model data available for this transfer", KACResources.styleContent, GUILayout.ExpandWidth(true));
                            }
                        }
                        catch (Exception ex)
                        {
                            GUILayout.Label("Unable to determine model data", KACResources.styleContent, GUILayout.ExpandWidth(true));
                            LogFormatted("Error determining model data: {0}", ex.Message);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Target", KACResources.styleAddSectionHeading, GUILayout.Width(55));
                    GUILayout.Label(new GUIContent("Phase Angle", "Displayed as \"Current Angle (Target Angle)\""), KACResources.styleAddSectionHeading, GUILayout.Width(105));
                    GUILayout.Label("Time to Transfer", KACResources.styleAddSectionHeading, GUILayout.ExpandWidth(true));
                    //GUILayout.Label("Time to Alarm", KACResources.styleAddSectionHeading, GUILayout.ExpandWidth(true));
                    GUILayout.Label("Add", KACResources.styleAddSectionHeading, GUILayout.Width(30));
                    GUILayout.EndHorizontal();

                    for (int intTarget = 0; intTarget < XferTargetBodies.Count; intTarget++)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(XferTargetBodies[intTarget].Target.bodyName, KACResources.styleAddXferName, GUILayout.Width(55), GUILayout.Height(20));
                        if (intXferCurrentParent != 0 || (!settings.XferUseModelData && settings.XferModelDataLoaded))
                        {
                            //formula based
                            String strPhase = String.Format("{0:0.00}({1:0.00})", XferTargetBodies[intTarget].PhaseAngleCurrent, XferTargetBodies[intTarget].PhaseAngleTarget);
                            GUILayout.Label(strPhase, KACResources.styleAddHeading, GUILayout.Width(105), GUILayout.Height(20));
                            GUILayout.Label(XferTargetBodies[intTarget].AlignmentTime.ToStringStandard(settings.TimeSpanFormat), KACResources.styleAddHeading, GUILayout.ExpandWidth(true), GUILayout.Height(20));
                        }
                        else 
                        { 
                            try
                            {
                                KACXFerModelPoint tmpModelPoint = KACResources.lstXferModelPoints.FirstOrDefault(
                                m => FlightGlobals.Bodies[m.Origin] == XferTargetBodies[intTarget].Origin &&
                                    FlightGlobals.Bodies[m.Target] == XferTargetBodies[intTarget].Target &&
                                    m.UT >= KACWorkerGameState.CurrentTime.UT);
                            
                                if (tmpModelPoint != null)
                                {
                                    String strPhase = String.Format("{0:0.00}({1:0.00})", XferTargetBodies[intTarget].PhaseAngleCurrent, tmpModelPoint.PhaseAngle);
                                    GUILayout.Label(strPhase, KACResources.styleAddHeading, GUILayout.Width(105), GUILayout.Height(20));
                                    KSPDateTime tmpTime = new KSPDateTime(tmpModelPoint.UT - KACWorkerGameState.CurrentTime.UT);
                                    GUILayout.Label(tmpTime.ToStringStandard(settings.DateTimeFormat), KACResources.styleAddHeading, GUILayout.ExpandWidth(true), GUILayout.Height(20));                                

                                    if (intTarget==intXferCurrentTarget)
                                        XferCurrentTargetEventTime = new KSPDateTime(tmpModelPoint.UT);
                                }
                                else
                                {
                                    GUILayout.Label("No future model data", KACResources.styleContent, GUILayout.ExpandWidth(true));
                                }
                            }
                            catch (Exception ex)
                            {
                                GUILayout.Label("Unable to determine model data", KACResources.styleContent, GUILayout.ExpandWidth(true));
                                LogFormatted("Error determining model data: {0}", ex.Message);
                            }
                        }
                        Boolean blnSelected = (intXferCurrentTarget == intTarget);
                        if (DrawToggle(ref blnSelected, "", KACResources.styleCheckbox, GUILayout.Width(42)))
                        {
                            if (blnSelected)
                            {
                                intXferCurrentTarget = intTarget;
                                BuildTransferStrings();
                            }
                        }

                        GUILayout.EndHorizontal();
                    }

                    intAddXferHeight += -56 + ( XferTargetBodies.Count * 30);
                }

                if (intXferCurrentParent != 0 || (!settings.XferUseModelData && settings.XferModelDataLoaded))
                {
                    //Formula based - add new alarm
                    if (DrawAddAlarm(new KSPDateTime(KACWorkerGameState.CurrentTime.UT + XferTargetBodies[intXferCurrentTarget].AlignmentTime.UT),
                                    XferTargetBodies[intXferCurrentTarget].AlignmentTime,
                                    new KSPTimeSpan(XferTargetBodies[intXferCurrentTarget].AlignmentTime.UT - timeMargin.UT)))
                    {
                        String strVesselID = "";
                        if (blnAlarmAttachToVessel) strVesselID = KACWorkerGameState.CurrentVessel.id.ToString();
                        alarms.Add(new KACAlarm(strVesselID, strAlarmName, strAlarmNotes + "\r\n\tMargin: " + new KSPTimeSpan(timeMargin.UT).ToStringStandard(TimeSpanStringFormatsEnum.IntervalLongTrimYears),
                            (KACWorkerGameState.CurrentTime.UT + XferTargetBodies[intXferCurrentTarget].AlignmentTime.UT - timeMargin.UT), timeMargin.UT, KACAlarm.AlarmTypeEnum.Transfer,
                            AddAction, XferTargetBodies[intXferCurrentTarget]));
                        //settings.Save();
                        _ShowAddPane = false;
                    }
                }
                else
                {
                    //Model based
                    if (XferCurrentTargetEventTime!=null)
                    {
                        if (DrawAddAlarm(XferCurrentTargetEventTime,
                                    new KSPTimeSpan(XferCurrentTargetEventTime.UT - KACWorkerGameState.CurrentTime.UT),
                                    new KSPTimeSpan(XferCurrentTargetEventTime.UT - KACWorkerGameState.CurrentTime.UT - timeMargin.UT),
                                    true))
                        {
                            String strVesselID = "";
                            if (blnAlarmAttachToVessel) strVesselID = KACWorkerGameState.CurrentVessel.id.ToString();
                            KACAlarm alarmNew = new KACAlarm(strVesselID, strAlarmName, (blnRepeatingXferAlarm ? "Alarm Repeats\r\n" : "") + strAlarmNotes + "\r\n\tMargin: " + new KSPTimeSpan(timeMargin.UT).ToStringStandard(TimeSpanStringFormatsEnum.IntervalLongTrimYears),
                                (XferCurrentTargetEventTime.UT - timeMargin.UT), timeMargin.UT, KACAlarm.AlarmTypeEnum.TransferModelled,
                                AddAction, XferTargetBodies[intXferCurrentTarget]); 
                            alarmNew.RepeatAlarm = blnRepeatingXferAlarm;
                            alarms.Add(alarmNew);
                            //settings.Save();
                            _ShowAddPane = false;
                        }
                    }
                    else{
                        GUILayout.Label("Selected a transfer with no event date",GUILayout.ExpandWidth(true));
                    }
                }
            }
            catch (Exception ex)
            {
                if (intXferCurrentTarget >= XferTargetBodies.Count) 
                    intXferCurrentTarget = 0;
                GUILayout.Label("Something weird has happened");
                LogFormatted(ex.Message);
                LogFormatted(ex.StackTrace);
            }

            //intAddXferHeight += intTestheight4;
            GUILayout.EndVertical();
        }



        private Int32 AddNotesHeight = 100;
        internal void FillAddMessagesWindow(int WindowID)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Vessel:", KACResources.styleAddHeading);
            String strVesselName = "Not Attached to Vessel";
            if (KACWorkerGameState.CurrentVessel != null && blnAlarmAttachToVessel) strVesselName = KACWorkerGameState.CurrentVessel.vesselName;
            GUILayout.TextField(strVesselName, KACResources.styleAddFieldGreen);
            GUILayout.Label("Alarm:", KACResources.styleAddHeading);
            strAlarmName = GUILayout.TextField(strAlarmName, KACResources.styleAddField, GUILayout.MaxWidth(184)).Replace("|", "");
            GUILayout.Label("Notes:", KACResources.styleAddHeading);
            strAlarmNotes = GUILayout.TextArea(strAlarmNotes, KACResources.styleAddMessageField,
                                GUILayout.Height(AddNotesHeight), GUILayout.MaxWidth(184)
                                ).Replace("|", ""); 

            GUILayout.EndVertical();
        }
    }
}
