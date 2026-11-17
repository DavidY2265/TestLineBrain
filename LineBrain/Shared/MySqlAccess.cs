using System;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 连 MySQL 这件事本身：建连接、试连通、把异常翻成人话。
    ///
    /// 报警和锁附各自的查询在 Alarm\ 和 Fasten\ 里，这里只放两边都要用的那点东西 ——
    /// 放各自那边就得抄两份，哪天改了一处忘了另一处，错法还不一样。
    ///
    /// **连不上不抛异常。** 产线上开机弹个异常栈比什么都不显示更糟，
    /// 所以出问题就往 DataProblems 记一条，看板照常起来，标题栏上写清楚。
    /// </summary>
    public static class MySqlAccess
    {


        /// <summary>
        /// 试连一下，给设置界面和排错用。不查任何表。
        /// </summary>
        public static bool TestConnection(BrainConfig config, out string message)
        {
            string? connectionString = config.BuildConnectionString();
            if (connectionString == null)
            {
                message = "连接信息不全或读不到密码，看 brain.csv 和 Windows 凭据";
                return false;
            }

            try
            {
                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    message = "连上了，服务器版本 " + connection.ServerVersion;
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = Describe(ex);
                return false;
            }
        }


        public static string TextOf(IDataRecord reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return "";
            }
            return reader.GetValue(index).ToString().Trim();
        }

        /// <summary>
        /// 异常转成一句人话。**不能把连接串原样打出来** —— 里面有密码。
        /// </summary>
        public static string Describe(Exception ex)
        {
            MySqlException? my = ex as MySqlException;
            if (my != null)
            {
                return "MySQL 错误 " + (int)my.ErrorCode + "：" + my.Message;
            }
            return ex.GetType().Name + "：" + ex.Message;
        }
    }
}
