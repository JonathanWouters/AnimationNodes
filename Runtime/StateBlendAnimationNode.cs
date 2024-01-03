using UnityEngine;

namespace AnimationNodes
{
	[System.Serializable]
	[CreateAssetMenu(menuName = "Animation Nodes/StateBlend")]
	public class StateBlendAnimationNode : ScriptableObject
	{
		public Object[] Nodes;
	}
}