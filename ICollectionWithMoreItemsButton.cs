using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace zzz.UI.Journals.Infrastructure.DataAccess.DataVirtualization
{
    public interface ICollectionWithMoreItemsButton
    {
        bool FirstItemIsVisible { get; set; }
    }
}
