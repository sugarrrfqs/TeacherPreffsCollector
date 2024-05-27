//------------------------------------------------------------------------------
// <auto-generated>
//     Этот код создан по шаблону.
//
//     Изменения, вносимые в этот файл вручную, могут привести к непредвиденной работе приложения.
//     Изменения, вносимые в этот файл вручную, будут перезаписаны при повторном создании кода.
// </auto-generated>
//------------------------------------------------------------------------------

namespace TeacherPreffsCollector
{
    using System;
    using System.Collections.Generic;
    
    public partial class Preference
    {
        public int ID { get; set; }
        public string TeacherID { get; set; }
        public string AuditoryInfo { get; set; } ///////////////////////////////
        [System.Text.Json.Serialization.JsonIgnoreAttribute]
        public Nullable<int> AuditoryID { get; set; }
        public Nullable<short> BCFirstWeek { get; set; }
        public Nullable<short> BCSecondWeek { get; set; }
        public Nullable<short> ACFirstWeek { get; set; }
        public Nullable<short> ACSecondWeek { get; set; }
        public string Weekdays { get; set; }
        public string TimeBegin { get; set; }
        public string TimeEnd { get; set; }
        public string DisciplineIDs { get; set; }
        public string DisciplineName { get; set; }
        public string DisciplineType { get; set; }
        public string Groups { get; set; }
        public Nullable<int> Subgroup { get; set; }
        public int StudentsCount { get; set; }
        public int Hours { get; set; }
        public Nullable<int> Stream { get; set; }

        [System.Text.Json.Serialization.JsonIgnoreAttribute]
        public virtual Auditory Auditory { get; set; }
        [System.Text.Json.Serialization.JsonIgnoreAttribute]
        public virtual Teacher Teacher { get; set; }
    }
}
