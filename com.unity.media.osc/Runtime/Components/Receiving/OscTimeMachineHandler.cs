#if RESOLVED_TIMEMACHINE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Iridescent.TimeMachine;
using UnityEngine;
using UnityEngine.Timeline;
using System.Text;
using System.Runtime.InteropServices;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.Media.Osc
{
    /// <summary>
    /// The base class to use for components that apply data from received OSC Messages to the Unity Scene.
    /// </summary>
    [AddComponentMenu("OSC/OSC TimeMachine Handler")]
    public class OscTimeMachineHandler : MonoBehaviour
    {
        [SerializeField, Tooltip("The OSC Receiver to handle messages from.")]
        OscReceiver m_Receiver;

        [SerializeField]
        TimeMachineTrackManager m_TimeMachine;

        [SerializeField]
        float m_GlobalPreWait = 0f;

            
        [Header("---  Move Section Event  ---")]
        public string moveSectionAddressPrefix = "/TimeMachine/MoveTo";
        public List<TimeMachineOscMoveSectionEvent> timeMachineOscMoveSectionEvents = new List<TimeMachineOscMoveSectionEvent>();
        
        [Header("---  TimeMachine Player Event  ---")]
        public string playerEventAddressPrefix = "/TimeMachine/Player";
        public List<TimeMachineOscPlayerOscEvent> timeMachineOscPlayerEvents = new List<TimeMachineOscPlayerOscEvent>();

        Coroutine offsetDelayCoroutine;

        OscCallbacks m_Callbacks;
        OscReceiver m_RegisteredReceiver;
        string m_RegisteredAddress;

        /// <summary>
        /// The OSC Receiver to handle messages from.
        /// </summary>
        public OscReceiver Receiver
        {
            get => m_Receiver;
            set
            {
                if (m_Receiver != value)
                {
                    m_Receiver = value;
                    RegisterCallbacks();
                }
            }
        }

        /// <summary>
        /// Resets the component to its default values.
        /// </summary>
        protected virtual void Reset()
        {
            Receiver = ComponentUtils.FindComponentInSameScene<OscReceiver>(gameObject);
            m_TimeMachine = ComponentUtils.FindComponentInSameScene<TimeMachineTrackManager>(gameObject);
        }

        /// <summary>
        /// Editor-only method called by Unity when the component is loaded or a value changes in the Inspector.
        /// </summary>
        protected virtual void OnValidate()
        {
            // OscUtils.ValidateAddress(ref m_Address, AddressType.Pattern);
            RegisterCallbacks();
        }

        /// <summary>
        /// This method is called by Unity when the component becomes enabled and active.
        /// </summary>
        protected virtual void OnEnable()
        {
            RegisterCallbacks();
        }

        /// <summary>
        /// This method is called by Unity when the component becomes disabled.
        /// </summary>
        protected virtual void OnDisable()
        {
            DeregisterCallbacks();
        }

        void RegisterCallbacks()
        {
            if (m_Receiver == m_RegisteredReceiver)
            {
                return;
            }

            DeregisterCallbacks();

            if (m_Receiver != null && timeMachineOscMoveSectionEvents.Count > 0)
            {
                if (m_Callbacks == null)
                    m_Callbacks = new OscCallbacks(ValueRead, MainThreadAction);

                foreach(var moveEvent in timeMachineOscMoveSectionEvents)
                {
                    m_Receiver.AddCallback(moveEvent.oscAddress, m_Callbacks);

                    m_RegisteredReceiver = m_Receiver;
                }
            }
        }

        void DeregisterCallbacks()
        {
            if (m_RegisteredReceiver != null && m_RegisteredAddress != null)
            {
                foreach(var moveEvent in timeMachineOscMoveSectionEvents)
                {
                    m_RegisteredReceiver.RemoveCallback(moveEvent.oscAddress, m_Callbacks);
                }
            }

            m_RegisteredReceiver = null;
            m_RegisteredAddress = null;
        }

        Queue<OscMessage> m_Messages = new(16);

        /// <inheritdoc />
        protected void ValueRead(OscMessage message)
        {
            m_Messages.Enqueue(message);
        }

        /// <inheritdoc />
        protected void MainThreadAction()
        {
            if (isActiveAndEnabled)
            {
                while(m_Messages.Count > 0)
                    OnMoveSectionReceived(m_Messages.Dequeue());
            }
            else
                m_Messages.Clear();
        }

        public void Init()
        {
            if(m_Receiver == null || m_TimeMachine == null) return;

            DeregisterCallbacks();

            timeMachineOscMoveSectionEvents.Clear();
            timeMachineOscPlayerEvents.Clear();

            var clipField = typeof(TimeMachineTrackManager).GetField("clips", BindingFlags.NonPublic | BindingFlags.Instance);
            var clips = (List<TimelineClip>)clipField.GetValue(m_TimeMachine);
            
            if(clips == null) return;

            foreach (var clip in clips)
            {
                var timeMachineControlClip = clip.asset as TimeMachineControlClip;
                timeMachineOscMoveSectionEvents.Add(new TimeMachineOscMoveSectionEvent
                {
                    oscAddress = $"{moveSectionAddressPrefix}/{timeMachineControlClip.sectionName}",
                    clipIndex = timeMachineControlClip.clipIndex,
                    sectionName = clip.displayName,
                });
            }

            timeMachineOscPlayerEvents.Add(new TimeMachineOscPlayerOscEvent
            {
                oscAddress = playerEventAddressPrefix + "/FinishCurrentRole",
                playerEvent = TimeMachinePlayerEventType.FinishCurrentRole,
            });

            timeMachineOscPlayerEvents.Add(new TimeMachineOscPlayerOscEvent
            {
                oscAddress = playerEventAddressPrefix + "/ResetAndReplay",
                playerEvent = TimeMachinePlayerEventType.ResetAndReplay,
            });

            timeMachineOscPlayerEvents.Add(new TimeMachineOscPlayerOscEvent
            {
                oscAddress = playerEventAddressPrefix + "/Stop",
                playerEvent = TimeMachinePlayerEventType.Stop,
            });
            
            timeMachineOscMoveSectionEvents.Sort((a, b) => a.clipIndex.CompareTo(b.clipIndex));

            RegisterCallbacks();
        }

        public unsafe void OnMoveSectionReceived(OscMessage message)
        {
            var address = message.GetAddressPattern();
            var addresses = stackalloc char[address.Length];
            var addressSpan = new ReadOnlySpan<char>(addresses, address.Length);
            Encoding.ASCII.GetChars(address.Pointer, address.Length, addresses, address.Length);
            
            foreach (var timeMachineOscEvent in timeMachineOscMoveSectionEvents)
            {
                if(!timeMachineOscEvent.oscAddress.AsSpan().SequenceEqual(addressSpan))
                    continue;

                var sectionName = timeMachineOscEvent.sectionName;

                var totalPreWait = m_GlobalPreWait;
                if(message.ArgumentCount > 0 && message.GetTag(0) == TypeTag.Float32)
                {
                    var floatValue = message.ReadFloat32(0);
                    if(!float.IsNaN(floatValue)) totalPreWait += floatValue;
                }
                
                if(offsetDelayCoroutine != null) StopCoroutine(offsetDelayCoroutine);
                var coroutineTime = Mathf.Max(totalPreWait, 0f);
                if (coroutineTime > 0)
                {
                    offsetDelayCoroutine = StartCoroutine(DelayMethod(coroutineTime, () =>
                    {
                        m_TimeMachine.MoveClip(sectionName, 0f);
                    }));
                }
                else if (coroutineTime == 0f)
                {
                    var offsetTime = totalPreWait < 0f ? Mathf.Abs(totalPreWait) : 0f;
                    m_TimeMachine.MoveClip(sectionName, offsetTime);
                }
            }
        }

        private IEnumerator DelayMethod(float waitTime, Action action)
        {
            yield return new WaitForSeconds(waitTime);
            action();
        }

        #if UNITY_EDITOR
        [CustomEditor(typeof(OscTimeMachineHandler))]
        public class OscTimeMachineHandlerEditor: Editor
        {
            public override void OnInspectorGUI()
            {
                OscTimeMachineHandler OscHandler = (OscTimeMachineHandler)target;
                
                // change check
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Receiver"));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_TimeMachine"), new GUIContent("TimeMachine Track Manager"));

                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_GlobalPreWait"),new GUIContent("Global Pre Wait (sec)"));
                var isEnable =
                    OscHandler.m_Receiver != null &&
                    OscHandler.m_TimeMachine != null;
                
                EditorGUI.BeginDisabledGroup(!isEnable);
                EditorGUILayout.Space();
                
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.Space();
                    if (GUILayout.Button("Initialize OSC Events", GUILayout.MaxWidth(200),GUILayout.Height(28)))
                    {
                        Undo.RecordObject(OscHandler, "Init TimeMachine Event");
                        OscHandler.Init();
                    }
                    EditorGUILayout.Space();
                }
            
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("moveSectionAddressPrefix"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("timeMachineOscMoveSectionEvents"));
            
                EditorGUILayout.PropertyField(serializedObject.FindProperty("playerEventAddressPrefix"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("timeMachineOscPlayerEvents"));
                
                EditorGUI.EndDisabledGroup();

                serializedObject.ApplyModifiedProperties();
            }
        }
        #endif
    }
}
#endif