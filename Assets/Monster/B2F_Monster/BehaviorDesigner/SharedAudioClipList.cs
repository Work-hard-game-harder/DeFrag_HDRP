using System.Collections.Generic;
using UnityEngine;

namespace BehaviorDesigner.Runtime
{
    [System.Serializable]
    public sealed class SharedAudioClipList : SharedVariable<List<AudioClip>>
    {
        public SharedAudioClipList()
        {
            mValue = new List<AudioClip>();
        }

        public static implicit operator SharedAudioClipList(List<AudioClip> value)
        {
            return new SharedAudioClipList { mValue = value };
        }
    }
}
