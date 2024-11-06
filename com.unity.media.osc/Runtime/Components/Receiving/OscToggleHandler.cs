using System;
using Unity.Media.Osc;
using UnityEngine;

namespace StageLab.Core
{
    /// <summary>
    /// The base class to use for components that apply data from received OSC Messages to the Unity Scene.
    /// </summary>
    [ExecuteAlways]
    public class OscToggleHandler : MonoBehaviour
    {
        [SerializeField, Tooltip("The OSC Receiver to handle messages from.")]
        OscReceiver m_Receiver;
        [SerializeField, Tooltip("The OSC Address Pattern to associate with this message handler.")]
        string m_Address = "/";

        [SerializeField]
        ArgumentHandlerVoid m_EnableArguments;

        [SerializeField]
        ArgumentHandlerVoid m_DisableArguments;

        OscCallbacks m_CallbacksEnable;
        OscCallbacks m_CallbacksDisable;
        OscReceiver m_RegisteredReceiver;
        string m_RegisteredAddress;

        const string ENABLE_SUFFIX = "/Enable";
        const string DISABlE_SUFFIX = "/Disable";

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
        /// The OSC Address Pattern to associate with this message handler.
        /// </summary>
        public string Address
        {
            get => m_Address;
            set
            {
                if (m_Address != value)
                {
                    m_Address = value;
                    OscUtils.ValidateAddress(ref m_Address, AddressType.Pattern);
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
            Address = $"/{name}";
        }

        /// <summary>
        /// Editor-only method called by Unity when the component is loaded or a value changes in the Inspector.
        /// </summary>
        protected virtual void OnValidate()
        {
            OscUtils.ValidateAddress(ref m_Address, AddressType.Pattern);
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
            if (m_Address == m_RegisteredAddress && m_Receiver == m_RegisteredReceiver)
            {
                return;
            }

            DeregisterCallbacks();

            if (m_Receiver != null && m_Address != null)
            {
                if (m_CallbacksEnable == null)
                    m_CallbacksEnable = new OscCallbacks(ValueReadEnable, MainThreadActionEnable);
                if (m_CallbacksDisable == null)
                    m_CallbacksDisable = new OscCallbacks(ValueReadDisable, MainThreadActionDisable);

                m_Receiver.AddCallback(m_Address+ENABLE_SUFFIX, m_CallbacksEnable);
                m_Receiver.AddCallback(m_Address+DISABlE_SUFFIX, m_CallbacksDisable);

                m_RegisteredReceiver = m_Receiver;
                m_RegisteredAddress = m_Address;
            }
        }

        void DeregisterCallbacks()
        {
            if (m_RegisteredReceiver != null && m_RegisteredAddress != null)
            {
                m_RegisteredReceiver.RemoveCallback(m_RegisteredAddress+ENABLE_SUFFIX, m_CallbacksEnable);
                m_RegisteredReceiver.RemoveCallback(m_RegisteredAddress+DISABlE_SUFFIX, m_CallbacksDisable);
            }

            m_RegisteredReceiver = null;
            m_RegisteredAddress = null;
        }

        /// <inheritdoc />
        protected void ValueReadEnable(OscMessage message)
        {
            var argIndex = 0;
            m_EnableArguments.Enqueue(message, argIndex);
        }

        /// <inheritdoc />
        protected void ValueReadDisable(OscMessage message)
        {
            var argIndex = 0;
            m_DisableArguments.Enqueue(message, argIndex);
        }

        /// <inheritdoc />
        protected void MainThreadActionEnable()
        {
            if (isActiveAndEnabled)
                m_EnableArguments.Invoke();
            else
                m_EnableArguments.Clear();
        }

        /// <inheritdoc />
        protected void MainThreadActionDisable()
        {
            if (isActiveAndEnabled)
                m_DisableArguments.Invoke();
            else
                m_DisableArguments.Clear();
        }

        /// <summary>
        /// Gets an argument handler according to its index.
        /// </summary>
        /// <param name="index">The index of the argument handler.</param>
        /// <returns>The argument handler, or <see langword="null"/> if <paramref name="index"/> is invalid.</returns>
        public ArgumentHandler GetArgument(int index)
        {
            if (index < 0 || 1 < index )
                return null;

            return index == 0? m_EnableArguments : m_DisableArguments;
        }
    }
}
