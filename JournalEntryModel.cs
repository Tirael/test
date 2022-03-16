namespace zzz.UI.Journals.Infrastructure.Models
{
    public class JournalEntryModel : IJournalEntry
    {
        #region Creation

        public static JournalEntryModel Create(JournalEntry e, IEUSettings euSettings, IEUNumericFormat numericFormat, Configuration.Configuration configuration)
        {
            return new JournalEntryModel
            {
                ID = e.ID,
                TimeStamp = e.TS,
                Message = e.Message,
                ShortMessage = e.MakeShortMessage(euSettings, numericFormat, configuration),
                ObjectCoordinateEnd = e.ObjectCoordinateEnd,
                ObjectCoordinateStart = e.ObjectCoordinateStart,
                ObjectID = e.ObjectID,
                Owner = e.Owner,
                Priority = e.Priority,
                SourceMessage = e.SourceMessage,
                SourceNumber = e.SourceNumber,
                MessageID = e.msgID,
                Related = e.Related,
                AckFlag = e.AckFlag,
                AckTimestamp = e.AckTimestamp,
                AckUser = e.AckUser,
                EmergencyLevel = e.emergencyLevel,
                MType = e.GetMtype(),
                Arguments = e.Arguments,
            };
        }

        protected JournalEntryModel()
        {
        }

        #endregion // Creation

        #region Implementation of IJournalEntry

        public int ID { get; set; }

        public DateTime TimeStamp { get; set; }

        public int SourceNumber { get; set; }

        public int SourceMessage { get; set; }

        public string Message { get; set; }
        public string ShortMessage { get; set; }

        public int Priority { get; set; }

        public int ObjectID { get; set; }

        public string ObjectName { get; set; }

        public double ObjectCoordinateStart { get; set; }

        public double ObjectCoordinateEnd { get; set; }

        public EventOwner Owner { get; set; }

        public int EmergencyLevel { get; set; }

        public int MType { get; set; }

        public Guid MessageID { get; set; }

        public HashSet<Guid> Related { get; set; }

        public bool AckFlag { get; set; }

        public DateTime AckTimestamp { get; set; }

        public string AckUser { get; set; }

        public List<IJEArg> Arguments { get; set; }

        #endregion
    }
}
