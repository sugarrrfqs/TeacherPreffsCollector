﻿//------------------------------------------------------------------------------
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
    using System.Data.Entity;
    using System.Data.Entity.Infrastructure;
    
    public partial class TeacherPrefsEntities : DbContext
    {
        public TeacherPrefsEntities()
            : base("name=TeacherPrefsEntities")
        {
        }
    
        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            throw new UnintentionalCodeFirstException();
        }
    
        public virtual DbSet<Auditory> Auditory { get; set; }
        public virtual DbSet<Preference> Preference { get; set; }
        public virtual DbSet<Teacher> Teacher { get; set; }
    }
}
