namespace AnimationNodes
{
	internal struct NodeData
	{
		public NodeType Type;
		public float Lenght;
		public int ChildCount;
		public int FirstChild;
		public int FirstPosition;
		public int ReadPosition;

		public enum NodeType
		{
			Clip,
			BlendStates,
			BlendLinear,
			BlendFreeformCartesian,
		}
	}
}