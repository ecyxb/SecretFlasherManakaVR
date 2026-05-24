using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SecretFlasherManakaVR.Runtime
{
    internal enum RuntimeEye
    {
        Left = 0,
        Right = 1
    }

    internal struct RuntimePose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public bool IsValid;

        public static RuntimePose Identity
        {
            get
            {
                return new RuntimePose
                {
                    Position = Vector3.zero,
                    Rotation = Quaternion.identity,
                    IsValid = true
                };
            }
        }
    }

    internal sealed class OpenVRBridgeReflection
    {
        private static readonly string[] PoseMethodNames =
        {
            "TryGetHmdPose",
            "TryGetHeadPose",
            "TryGetPose",
            "GetHmdPose",
            "GetHeadPose",
            "GetPose",
            "WaitGetPoses"
        };

        private static readonly string[] SubmitMethodNames =
        {
            "Submit",
            "SubmitEye",
            "SubmitTexture",
            "SubmitRenderTexture"
        };

        private static readonly string[] ProjectionMethodNames =
        {
            "TryGetProjectionMatrix",
            "GetProjectionMatrix",
            "GetProjection"
        };

        private readonly object bridge;
        private readonly Type bridgeType;

        public OpenVRBridgeReflection(object bridge)
        {
            this.bridge = bridge;
            bridgeType = bridge.GetType();
        }

        public bool Initialize(bool autoStartSteamVR, IVrRuntimeLogger logger, out string message)
        {
            message = string.Empty;
            MethodInfo method = FindMethod("Initialize", "Init", "Start");
            if (method == null)
            {
                return ReadBoolProperty("IsInitialized", "Initialized", "IsAvailable", "Available", true);
            }

            object result;
            try
            {
                object[] args = BuildArguments(method, autoStartSteamVR);
                result = method.Invoke(bridge, args);
            }
            catch (Exception ex)
            {
                message = ex.GetBaseException().Message;
                return false;
            }

            bool success;
            if (TryReadInitResult(result, out success, out message))
            {
                return success;
            }

            if (result is bool)
            {
                return (bool)result;
            }

            return ReadBoolProperty("IsInitialized", "Initialized", "IsAvailable", "Available", true);
        }

        public bool IsReady()
        {
            return ReadBoolProperty("IsInitialized", "Initialized", "IsAvailable", "Available", true);
        }

        public bool TryGetRecommendedRenderTargetSize(out int width, out int height)
        {
            width = 0;
            height = 0;

            MethodInfo method = FindMethod("GetRecommendedRenderTargetSize", "GetRenderTargetSize", "GetRecommendedSize");
            if (method == null)
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];
            try
            {
                object result = method.Invoke(bridge, args);
                if (ReadWidthHeight(result, out width, out height))
                {
                    return true;
                }

                if (args.Length >= 2)
                {
                    width = Convert.ToInt32(args[0]);
                    height = Convert.ToInt32(args[1]);
                    return width > 0 && height > 0;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public bool TryGetHmdPose(out RuntimePose pose)
        {
            pose = RuntimePose.Identity;

            for (int i = 0; i < PoseMethodNames.Length; i++)
            {
                MethodInfo method = FindMethod(PoseMethodNames[i]);
                if (method == null)
                {
                    continue;
                }

                if (TryInvokePoseMethod(method, out pose))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetProjectionMatrix(RuntimeEye eye, float nearClip, float farClip, out Matrix4x4 projection)
        {
            projection = Matrix4x4.identity;
            for (int i = 0; i < ProjectionMethodNames.Length; i++)
            {
                MethodInfo method = FindMethod(ProjectionMethodNames[i]);
                if (method == null)
                {
                    continue;
                }

                if (TryInvokeProjectionMethod(method, eye, nearClip, farClip, out projection))
                {
                    return true;
                }
            }

            return false;
        }

        public float GetIpdMeters(float fallback)
        {
            MethodInfo method = FindMethod("GetIpdMeters", "GetIPD", "GetIpd", "GetInterpupillaryDistance");
            if (method != null && method.GetParameters().Length == 0)
            {
                try
                {
                    object value = method.Invoke(bridge, null);
                    return Convert.ToSingle(value);
                }
                catch
                {
                    return fallback;
                }
            }

            return ReadFloatProperty(fallback, "IpdMeters", "IPD", "Ipd", "InterpupillaryDistance");
        }

        public bool Submit(RuntimeEye eye, RenderTexture texture)
        {
            if (texture == null)
            {
                return false;
            }

            for (int i = 0; i < SubmitMethodNames.Length; i++)
            {
                MethodInfo[] candidates = bridgeType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(method => method.Name == SubmitMethodNames[i])
                    .ToArray();

                for (int j = 0; j < candidates.Length; j++)
                {
                    if (TryInvokeSubmitMethod(candidates[j], eye, texture))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void BeginFrame()
        {
            MethodInfo method = FindMethod("BeginFrame", "ClearLastSubmittedFrame");
            if (method == null || method.GetParameters().Length != 0)
            {
                return;
            }

            try
            {
                method.Invoke(bridge, null);
            }
            catch
            {
            }
        }

        public string GetLastError()
        {
            object value = ReadMember(bridgeType, bridge, "LastError", "Error", "ErrorMessage", "LastSubmitError");
            return value == null ? string.Empty : value.ToString();
        }

        public void Shutdown()
        {
            MethodInfo method = FindMethod("Shutdown", "Dispose", "Stop");
            if (method == null || method.GetParameters().Length != 0)
            {
                return;
            }

            try
            {
                method.Invoke(bridge, null);
            }
            catch
            {
            }
        }

        private bool TryInvokePoseMethod(MethodInfo method, out RuntimePose pose)
        {
            pose = RuntimePose.Identity;
            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];

            try
            {
                object result = method.Invoke(bridge, args);
                if (result is bool && parameters.Length > 0)
                {
                    if (!(bool)result)
                    {
                        return false;
                    }

                    return TryReadPose(args[0], out pose);
                }

                if (TryReadPose(result, out pose))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private bool TryInvokeProjectionMethod(MethodInfo method, RuntimeEye eye, float nearClip, float farClip, out Matrix4x4 projection)
        {
            projection = Matrix4x4.identity;
            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];

            if (!FillProjectionArguments(parameters, args, eye, nearClip, farClip))
            {
                return false;
            }

            try
            {
                object result = method.Invoke(bridge, args);
                if (result is bool)
                {
                    if (!(bool)result)
                    {
                        return false;
                    }

                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] is Matrix4x4)
                        {
                            projection = (Matrix4x4)args[i];
                            return true;
                        }
                    }

                    return false;
                }

                if (result is Matrix4x4)
                {
                    projection = (Matrix4x4)result;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private bool TryInvokeSubmitMethod(MethodInfo method, RuntimeEye eye, RenderTexture texture)
        {
            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];

            if (!FillSubmitArguments(parameters, args, eye, texture))
            {
                return false;
            }

            try
            {
                object result = method.Invoke(bridge, args);
                return !(result is bool) || (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private bool FillProjectionArguments(ParameterInfo[] parameters, object[] args, RuntimeEye eye, float nearClip, float farClip)
        {
            bool eyeSet = false;
            bool nearSet = false;
            bool farSet = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = GetParameterType(parameters[i]);
                if (!eyeSet && TryMakeEyeArgument(type, eye, out args[i]))
                {
                    eyeSet = true;
                }
                else if (!nearSet && IsFloatLike(type))
                {
                    args[i] = ConvertNumber(nearClip, type);
                    nearSet = true;
                }
                else if (!farSet && IsFloatLike(type))
                {
                    args[i] = ConvertNumber(farClip, type);
                    farSet = true;
                }
                else if (parameters[i].ParameterType.IsByRef)
                {
                    args[i] = null;
                }
                else
                {
                    return false;
                }
            }

            return eyeSet && nearSet && farSet;
        }

        private bool FillSubmitArguments(ParameterInfo[] parameters, object[] args, RuntimeEye eye, RenderTexture texture)
        {
            bool eyeSet = false;
            bool textureSet = false;
            bool widthSet = false;
            bool heightSet = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = GetParameterType(parameters[i]);
                if (!eyeSet && TryMakeEyeArgument(type, eye, out args[i]))
                {
                    eyeSet = true;
                }
                else if (!textureSet && type.IsAssignableFrom(texture.GetType()))
                {
                    args[i] = texture;
                    textureSet = true;
                }
                else if (!textureSet && type == typeof(Texture))
                {
                    args[i] = texture;
                    textureSet = true;
                }
                else if (!textureSet && type == typeof(IntPtr))
                {
                    args[i] = texture.GetNativeTexturePtr();
                    textureSet = true;
                }
                else if (!widthSet && IsIntegerLike(type))
                {
                    args[i] = ConvertNumber(texture.width, type);
                    widthSet = true;
                }
                else if (!heightSet && IsIntegerLike(type))
                {
                    args[i] = ConvertNumber(texture.height, type);
                    heightSet = true;
                }
                else
                {
                    return false;
                }
            }

            return eyeSet && textureSet;
        }

        private object[] BuildArguments(MethodInfo method, bool autoStartSteamVR)
        {
            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = GetParameterType(parameters[i]);
                if (type == typeof(bool))
                {
                    args[i] = autoStartSteamVR;
                }
                else
                {
                    args[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
                }
            }

            return args;
        }

        private bool TryMakeEyeArgument(Type parameterType, RuntimeEye eye, out object value)
        {
            value = null;
            if (parameterType.IsEnum)
            {
                string name = eye == RuntimeEye.Left ? "Left" : "Right";
                value = Enum.Parse(parameterType, name);
                return true;
            }

            if (parameterType == typeof(string))
            {
                value = eye == RuntimeEye.Left ? "Left" : "Right";
                return true;
            }

            if (IsIntegerLike(parameterType))
            {
                value = ConvertNumber((int)eye, parameterType);
                return true;
            }

            return false;
        }

        private bool TryReadInitResult(object result, out bool success, out string message)
        {
            success = false;
            message = string.Empty;
            if (result == null)
            {
                return false;
            }

            Type type = result.GetType();
            object successValue = ReadMember(type, result, "Success", "Succeeded", "IsSuccess", "IsInitialized");
            object messageValue = ReadMember(type, result, "Message", "ErrorMessage", "Error", "FailureReason");

            if (successValue == null)
            {
                return false;
            }

            success = Convert.ToBoolean(successValue);
            message = messageValue == null ? string.Empty : messageValue.ToString();
            return true;
        }

        private bool TryReadPose(object value, out RuntimePose pose)
        {
            pose = RuntimePose.Identity;
            if (value == null)
            {
                return false;
            }

            Type type = value.GetType();
            object position = ReadMember(type, value, "Position", "HeadPosition", "HmdPosition", "LocalPosition");
            object rotation = ReadMember(type, value, "Rotation", "HeadRotation", "HmdRotation", "LocalRotation");
            object valid = ReadMember(type, value, "IsValid", "Valid", "PoseIsValid");

            if (position is Vector3)
            {
                pose.Position = (Vector3)position;
            }

            if (rotation is Quaternion)
            {
                pose.Rotation = (Quaternion)rotation;
            }

            pose.IsValid = valid == null || Convert.ToBoolean(valid);
            return pose.IsValid;
        }

        private bool ReadWidthHeight(object value, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (value == null)
            {
                return false;
            }

            if (value is Vector2Int)
            {
                Vector2Int vector = (Vector2Int)value;
                width = vector.x;
                height = vector.y;
                return width > 0 && height > 0;
            }

            if (value is Vector2)
            {
                Vector2 vector = (Vector2)value;
                width = Mathf.RoundToInt(vector.x);
                height = Mathf.RoundToInt(vector.y);
                return width > 0 && height > 0;
            }

            Type type = value.GetType();
            object widthValue = ReadMember(type, value, "Width", "width", "X", "x");
            object heightValue = ReadMember(type, value, "Height", "height", "Y", "y");
            if (widthValue == null || heightValue == null)
            {
                return false;
            }

            width = Convert.ToInt32(widthValue);
            height = Convert.ToInt32(heightValue);
            return width > 0 && height > 0;
        }

        private object ReadMember(Type type, object instance, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                PropertyInfo property = type.GetProperty(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    return property.GetValue(instance, null);
                }

                FieldInfo field = type.GetField(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(instance);
                }
            }

            return null;
        }

        private bool ReadBoolProperty(string name1, string name2, string name3, string name4, bool fallback)
        {
            object value = ReadMember(bridgeType, bridge, name1, name2, name3, name4);
            return value == null ? fallback : Convert.ToBoolean(value);
        }

        private float ReadFloatProperty(float fallback, params string[] names)
        {
            object value = ReadMember(bridgeType, bridge, names);
            return value == null ? fallback : Convert.ToSingle(value);
        }

        private MethodInfo FindMethod(params string[] names)
        {
            MethodInfo[] methods = bridgeType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < names.Length; i++)
            {
                for (int j = 0; j < methods.Length; j++)
                {
                    if (methods[j].Name == names[i])
                    {
                        return methods[j];
                    }
                }
            }

            return null;
        }

        private static Type GetParameterType(ParameterInfo parameter)
        {
            Type type = parameter.ParameterType;
            return type.IsByRef ? type.GetElementType() : type;
        }

        private static bool IsFloatLike(Type type)
        {
            return type == typeof(float) || type == typeof(double);
        }

        private static bool IsIntegerLike(Type type)
        {
            return type == typeof(int) || type == typeof(uint) || type == typeof(long) ||
                   type == typeof(ulong) || type == typeof(short) || type == typeof(ushort);
        }

        private static object ConvertNumber(float value, Type type)
        {
            return Convert.ChangeType(value, type);
        }

        private static object ConvertNumber(int value, Type type)
        {
            return Convert.ChangeType(value, type);
        }
    }
}
