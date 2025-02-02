namespace DroplerGUI.Services.Steam
{
	public class Confirmation
	{
		/// <summary>
		/// The ID of this confirmation
		/// </summary>
		public ulong ID { get; private set; }

		/// <summary>
		/// The unique key used to act upon this confirmation.
		/// </summary>
		public ulong Key { get; private set; }

		/// <summary>
		/// The value of the data-type HTML attribute returned for this contribution.
		/// </summary>
		public int ConfType { get; private set; }

		/// <summary>
		/// Represents either the Trade Offer ID or market transaction ID that caused this confirmation to be created.
		/// </summary>
		public ulong Creator { get; private set; }

		public Confirmation(ulong id, ulong key, int confType, ulong creator)
		{
			this.ID = id;
			this.Key = key;
			this.ConfType = confType;
			this.Creator = creator;
		}

		public enum ConfirmationType
		{
			Trade = 1,
			MarketListing = 3
		}
	}
}
