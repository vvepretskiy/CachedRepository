using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CachedRepository
{
	class Program
	{
		static void Main(string[] args)
		{
			try
			{
				IUserRepository userRepository = new UserRepository();
				IProductRepository productRepository = new ProductRepository();
				CachedRepository cachedRepository = new CachedRepository(userRepository, productRepository);

				User user = cachedRepository.GetUser("user1");
				Thread.Sleep(6000);
				User newUser = cachedRepository.GetUser("user1");
				if (user.Id == newUser.Id)
				{
					Console.WriteLine("Cache doesn't work: new user has not been created");
					return;
				}
				Product product = cachedRepository.GetProduct("prod1");
				Thread.Sleep(2000);
				User newProduct = cachedRepository.GetUser("prod1");
				if (product.Id == newProduct.Id)
				{
					Console.WriteLine("Cache doesn't work: new product has been created");
					return;
				}
				Console.WriteLine("Cache works correct");
			}
			finally
			{
				Console.ReadKey();
			}
		}
	}

	public class User
	{
		public int Id { get; set; }
		public string FirstName { get; set; }
		public string SecondName { get; set; }
	}

	public interface IUserRepository
	{
		User GetUser(string key);
	}

	public class UserRepository : IUserRepository
	{
		public User GetUser(string key)
		{
			return new User { FirstName = "FirstName", Id = new Random().Next(), SecondName = "SecondName" };
		}
	}

	public class Product
	{
		public int Id { get; set; }
		public string Name { get; set; }
	}

	public interface IProductRepository
	{
		Product GetProduct(string key);
	}

	public class ProductRepository : IProductRepository
	{
		public Product GetProduct(string key)
		{
			return new Product { Name = "Name", Id = new Random().Next() };
		}
	}

	public class CacheObject
	{
		public string Key { get; set; }
		public object Data { get; set; }
		public DateTime CreationDate { get; set; }
	}

	// We could implement CachedRepository as generic and had own CachedRepository for every
	// specific Repositiry. But I've decided to implement one general CachedRepository wich
	// will be inherited from differents specific repository. Of course in this situation we are
	// getting cons such as boxing and unboxing objects, but talking about profit, it is easy scalability.
	// Architecture of build CachedRepository is based on DI\IoC pattern, to cover this by UnitTest
	public class CachedRepository : IUserRepository, IProductRepository
	{
		private readonly ReaderWriterLockSlim _cacheLock = 
			new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

		private readonly IUserRepository _userRepository;
		private readonly IProductRepository _productRepository;

		private readonly List<CacheObject> _cache = new List<CacheObject>();

		private int _cacheDuration = 5;

		public CachedRepository(IUserRepository userRepository, IProductRepository productRepository)
		{
			_userRepository = userRepository;
			_productRepository = productRepository;
		}

		private T GetData<T>(string key, Func<string, T> getData) where T : class
		{
			// Enter an upgradeable read lock because we might have to use a write lock 
			// if having to update the cache
			// Multiple threads can read the cache at the same time
			_cacheLock.EnterUpgradeableReadLock();
			try
			{
				CacheObject cachedObject = _cache.SingleOrDefault(u => u.Key == key);
				
				// object has been found
				if (cachedObject != null)
				{
					// if cache is outdated then remove value from it
					if (cachedObject.CreationDate.AddSeconds(_cacheDuration) < DateTime.Now)
					{
						// Upgrade to a write lock, as an item has to be removed from the cache.
						// We will only enter the write lock if nobody holds either a read or write lock
						_cacheLock.EnterWriteLock();
						try
						{
							_cache.Remove(cachedObject);
						}
						finally
						{
							_cacheLock.ExitWriteLock();
						}
					}
					else
					{
						// update cache date
						cachedObject.CreationDate = DateTime.Now;
						T resultData = cachedObject.Data as T;
						if (resultData != null)
						{
							return resultData;
						}
					}
				}

				T result = getData(key);
				// Upgrade to a write lock, as an item will (probably) be added to the cache.
				// We will only enter the write lock if nobody holds either a read or write lock
				_cacheLock.EnterWriteLock();
				try
				{
					if (_cache.Any(u => u.Key != key))
					{
						_cache.Add(new CacheObject { Data = result, Key = key, CreationDate = DateTime.Now });
					}
				}
				finally
				{
					_cacheLock.ExitWriteLock();
				}
				return result;
			}
			finally
			{
				_cacheLock.ExitUpgradeableReadLock();
			}
		}

		public User GetUser(string key)
		{
			return GetData(key, _userRepository.GetUser);
		}

		public Product GetProduct(string key)
		{
			return GetData(key, _productRepository.GetProduct);
		}
	}
}
