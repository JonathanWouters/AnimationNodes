using UnityEngine;


namespace AnimationNodes
{
	[System.Serializable]
	[CreateAssetMenu(menuName = "Animation Nodes/Blend1D")]
	public class Blend1DAnimationNode : ScriptableObject
	{
		[System.Serializable]
		public struct Data
		{
			public Object Node;
			public float Position;
		}

		public Data[] Nodes;
	}
}