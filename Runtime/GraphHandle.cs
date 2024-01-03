namespace AnimationNodes
{
	public struct GraphHandle
	{
		internal const int INVALID_INDEX = -1;
		private int _index;

		internal int Index // Trickery to handle default value of index being 0
		{ 
			get => _index - 1;
			set => _index = value + 1;
		}

		public bool IsValid => Index != INVALID_INDEX;

		public static implicit operator int(GraphHandle handle) => handle.Index;

		public static readonly GraphHandle Invalid = new GraphHandle();
	}
}