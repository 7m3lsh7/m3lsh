﻿using Microsoft.EntityFrameworkCore;
using webchat.Models;

namespace webchat.data
{
    public class ChatDbcontect :  DbContext
    {
        public ChatDbcontect(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Admin> admins { get; set; }
        public DbSet<User> users { get; set; }

    }
}
