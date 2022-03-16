namespace zzz.UI.Journals.Infrastructure.Interfaces
{
	public interface IIntID
    {
        /// <summary>
        /// Идентификатор
        /// </summary>
        int ID { get; }
    }
	
    public interface IJournalEntry : IIntID
    {
        /// <summary>
        /// Метка времени сообщения.
        /// </summary>
        DateTime TimeStamp { get; set; }

        /// <summary>
        /// Переменная, содержащая в себе информацию об алгоритме, из которого получено сообщение.
        /// </summary>
        int SourceNumber { get; set; }

        /// <summary>
        /// Переменная, содержащая в себе информацию о типе сообщения.
        /// </summary>
        int SourceMessage { get; set; }

        /// <summary>
        /// Текст сообщения.
        /// </summary>
        string Message { get; set; }

        /// <summary>
        /// Сокращенный текст сообщения.
        /// </summary>
        string ShortMessage { get; set; }

        /// <summary>
        /// Приоритет сообщения
        /// </summary>
        int Priority { get; set; }

        /// <summary>
        /// Идентификатор объекта, по которому выдано сообщение.
        /// </summary>
        int ObjectID { get; set; }

        /// <summary>
        /// Наименование объекта, по которому выдано сообщение.
        /// </summary>
        string ObjectName { get; }

        /// <summary>
        /// Координата объекта, по которому выдано сообщение.
        /// Массив, который может состоять из одного элемента (для сосредоточенных объектов)
        /// или из двух элементов (для распределенных объектов).
        /// </summary>
        double ObjectCoordinateStart { get; set; }

        /// <summary>
        /// Координата объекта, по которому выдано сообщение.
        /// Массив, который может состоять из одного элемента (для сосредоточенных объектов)
        /// или из двух элементов (для распределенных объектов).
        /// </summary>
        double ObjectCoordinateEnd { get; set; }

        /// <summary>
        /// Принадлежность сообщения
        /// </summary>
        EventOwner Owner { get; set; }

        int EmergencyLevel { get; set; }

        int MType { get; set; }

        Guid MessageID { get; }

        /// <summary>
        /// Сообщения, связанные в этим. Данное поле присутствует только у сообщений с priority==5.
        /// Это массив, который содержит guid сообщений, связанных с данным сообщением.
        /// </summary>
        HashSet<Guid> Related { get; set; }

        /// <summary>
        /// Факт квитации (true-сообщение квитировано, false – сообщение не квитировано).
        /// </summary>
        bool AckFlag { get; set; }

        /// <summary>
        /// Время квитации.
        /// </summary>
        DateTime AckTimestamp { get; set; }

        /// <summary>
        /// Имя пользователя, квитировавшего сообщение.
        /// </summary>
        string AckUser { get; set; }

        /// <summary>
        /// Список аргументов для форматирования сообщения
        /// </summary>
        List<IJEArg> Arguments { get; set; }
    }
}
