﻿/*
 *  Dover Framework - OpenSource Development framework for SAP Business One
 *  Copyright (C) 2014  Eduardo Piva
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <http://www.gnu.org/licenses/>.
 *  
 *  Contact me at <efpiva@gmail.com>
 * 
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dover.Framework.Monad
{
    internal static class StringExtension
    {
        internal static String Left(this String s, Int32 length)
        {
            if (s.Length <= length)
                return s;

            return s.Substring(0, length);
        }

        internal static String Right(this String s, Int32 length)
        {
            if (s.Length <= length)
                return s;
            return s.Substring(s.Length - length, length);
        }

        internal static String Truncate(this String s, Int32 length)
        {
            return s.Length > length ? s.Substring(0, length) : s;
        }

    }
}
