using Unity.Mathematics;
using UnityEngine;

namespace AnimationNodes
{
	[System.Serializable]
	[CreateAssetMenu(menuName = "Animation Nodes/Blend2D")]
	public class Blend2DAnimationNode : ScriptableObject
	{
		[System.Serializable]
		public struct Data
		{
			public Object Node;
			public float2 Position;
		}

		public Data[] Nodes;
	}
}