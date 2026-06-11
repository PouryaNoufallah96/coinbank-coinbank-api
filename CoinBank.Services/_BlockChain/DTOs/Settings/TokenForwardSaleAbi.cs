namespace CoinBank.Services._BlockChain.DTOs.Settings
{

    public static class TokenForwardSaleAbi
    {
        public const string PreSaleAbi = @"
       [
    {
        ""type"": ""function"",
        ""name"": ""claimTokensByOperator"",
        ""inputs"": [
            {
                ""name"": ""presaleId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""presaleId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""function"",
        ""name"": ""configurePresale"",
        ""inputs"": [
            {
                ""name"": ""saleId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""token"",
                ""type"": ""address"",
                ""internalType"": ""address""
            },
            {
                ""name"": ""totalAllocation"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""maxPerWallet"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""start"",
                ""type"": ""uint64"",
                ""internalType"": ""uint64""
            },
            {
                ""name"": ""end"",
                ""type"": ""uint64"",
                ""internalType"": ""uint64""
            },
            {
                ""name"": ""vestingData"",
                ""type"": ""tuple[]"",
                ""internalType"": ""struct PresaleTypes.VestingCheckpoint[]"",
                ""components"": [
                    {
                        ""name"": ""timestamp"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""bps"",
                        ""type"": ""uint16"",
                        ""internalType"": ""uint16""
                    }
                ]
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""function"",
        ""name"": ""eip712Domain"",
        ""inputs"": [],
        ""outputs"": [
            {
                ""name"": ""fields"",
                ""type"": ""bytes1"",
                ""internalType"": ""bytes1""
            },
            {
                ""name"": ""name"",
                ""type"": ""string"",
                ""internalType"": ""string""
            },
            {
                ""name"": ""version"",
                ""type"": ""string"",
                ""internalType"": ""string""
            },
            {
                ""name"": ""chainId"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""verifyingContract"",
                ""type"": ""address"",
                ""internalType"": ""address""
            },
            {
                ""name"": ""salt"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""extensions"",
                ""type"": ""uint256[]"",
                ""internalType"": ""uint256[]""
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""function"",
        ""name"": ""getPresaleInfo"",
        ""inputs"": [
            {
                ""name"": ""presaleId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [
            {
                ""name"": """",
                ""type"": ""tuple"",
                ""internalType"": ""struct PresaleTypes.SaleConfig"",
                ""components"": [
                    {
                        ""name"": ""token"",
                        ""type"": ""address"",
                        ""internalType"": ""address""
                    },
                    {
                        ""name"": ""allocation"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""sold"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""maxPerWallet"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""start"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""end"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""initialized"",
                        ""type"": ""bool"",
                        ""internalType"": ""bool""
                    }
                ]
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""function"",
        ""name"": ""getUserAllocation"",
        ""inputs"": [
            {
                ""name"": ""user"",
                ""type"": ""address"",
                ""internalType"": ""address""
            },
            {
                ""name"": ""presaleId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [
            {
                ""name"": """",
                ""type"": ""tuple"",
                ""internalType"": ""struct UserTypes.UserAllocation"",
                ""components"": [
                    {
                        ""name"": ""purchased"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""claimed"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""purchaseCount"",
                        ""type"": ""uint32"",
                        ""internalType"": ""uint32""
                    }
                ]
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""function"",
        ""name"": ""purchaseTokens"",
        ""inputs"": [
            {
                ""name"": ""saleId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""orderId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""receiveAmount"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""paymentToken"",
                ""type"": ""address"",
                ""internalType"": ""address""
            },
            {
                ""name"": ""paymentAmount"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""deadline"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""signature"",
                ""type"": ""bytes"",
                ""internalType"": ""bytes""
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""event"",
        ""name"": ""Claimed"",
        ""inputs"": [
            {
                ""name"": ""saleId"",
                ""type"": ""bytes32"",
                ""indexed"": false,
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""buyer"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""amountClaimed"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            }
        ],
        ""anonymous"": false
    },
    {
        ""type"": ""event"",
        ""name"": ""PresaleConfigured"",
        ""inputs"": [
            {
                ""name"": ""saleId"",
                ""type"": ""bytes32"",
                ""indexed"": false,
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""token"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""totalAllocation"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""maxPerWallet"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""start"",
                ""type"": ""uint64"",
                ""indexed"": false,
                ""internalType"": ""uint64""
            },
            {
                ""name"": ""end"",
                ""type"": ""uint64"",
                ""indexed"": false,
                ""internalType"": ""uint64""
            }
        ],
        ""anonymous"": false
    },
    {
        ""type"": ""event"",
        ""name"": ""Purchased"",
        ""inputs"": [
            {
                ""name"": ""saleId"",
                ""type"": ""bytes32"",
                ""indexed"": false,
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""orderId"",
                ""type"": ""bytes32"",
                ""indexed"": false,
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""buyer"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""amountPurchased"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""amountPaid"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            }
        ],
        ""anonymous"": false
    },
    {
        ""type"": ""function"",
        ""name"": ""nonces"",
        ""inputs"": [
            {
                ""name"": ""user"",
                ""type"": ""address"",
                ""internalType"": ""address""
            },
            {
                ""name"": ""saleId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [
            {
                ""name"": ""nonce"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            }
        ],
        ""stateMutability"": ""view""
    }
]
    ";

        public const string SwapAbi = @"[
  {
    ""type"": ""function"",
    ""name"": ""estimateFee"",
    ""inputs"": [
      {
        ""name"": ""params"",
        ""type"": ""tuple"",
        ""internalType"": ""struct SwapTypes.SwapParams"",
        ""components"": [
          {
            ""name"": ""swapId"",
            ""type"": ""bytes32"",
            ""internalType"": ""bytes32""
          },
          {
            ""name"": ""dstEid"",
            ""type"": ""uint32"",
            ""internalType"": ""uint32""
          },
          {
            ""name"": ""tokenIn"",
            ""type"": ""address"",
            ""internalType"": ""address""
          },
          {
            ""name"": ""tokenOut"",
            ""type"": ""address"",
            ""internalType"": ""address""
          },
          {
            ""name"": ""amountIn"",
            ""type"": ""uint256"",
            ""internalType"": ""uint256""
          },
          {
            ""name"": ""minAmountOut"",
            ""type"": ""uint256"",
            ""internalType"": ""uint256""
          },
          {
            ""name"": ""receiver"",
            ""type"": ""address"",
            ""internalType"": ""address""
          },
          {
            ""name"": ""path"",
            ""type"": ""address[]"",
            ""internalType"": ""address[]""
          }
        ]
      },
      {
        ""name"": ""sendOptions"",
        ""type"": ""bytes"",
        ""internalType"": ""bytes""
      },
      {
        ""name"": ""returnOptions"",
        ""type"": ""bytes"",
        ""internalType"": ""bytes""
      },
      {
        ""name"": ""returnNativeDrop"",
        ""type"": ""uint128"",
        ""internalType"": ""uint128""
      }
    ],
    ""outputs"": [
      {
        ""name"": """",
        ""type"": ""uint256"",
        ""internalType"": ""uint256""
      }
    ],
    ""stateMutability"": ""view""
  },
  {
    ""type"": ""function"",
    ""name"": ""estimateReturnFee"",
    ""inputs"": [
      {
        ""name"": ""srcEid"",
        ""type"": ""uint32"",
        ""internalType"": ""uint32""
      },
      {
        ""name"": ""returnOptions"",
        ""type"": ""bytes"",
        ""internalType"": ""bytes""
      }
    ],
    ""outputs"": [
      {
        ""name"": """",
        ""type"": ""uint256"",
        ""internalType"": ""uint256""
      }
    ],
    ""stateMutability"": ""view""
  },
  {
    ""type"": ""function"",
    ""name"": ""getOutputAmount"",
    ""inputs"": [
      {
        ""name"": ""srcEid"",
        ""type"": ""uint32"",
        ""internalType"": ""uint32""
      },
      {
        ""name"": ""dstEid"",
        ""type"": ""uint32"",
        ""internalType"": ""uint32""
      },
      {
        ""name"": ""amountIn"",
        ""type"": ""uint256"",
        ""internalType"": ""uint256""
      },
      {
        ""name"": ""path"",
        ""type"": ""address[]"",
        ""internalType"": ""address[]""
      }
    ],
    ""outputs"": [
      {
        ""name"": """",
        ""type"": ""uint256"",
        ""internalType"": ""uint256""
      }
    ],
    ""stateMutability"": ""view""
  },
  {
    ""type"": ""function"",
    ""name"": ""getLiquidityBalance"",
    ""inputs"": [
      {
        ""name"": ""token"",
        ""type"": ""address"",
        ""internalType"": ""address""
      }
    ],
    ""outputs"": [
      {
        ""name"": """",
        ""type"": ""uint256"",
        ""internalType"": ""uint256""
      }
    ],
    ""stateMutability"": ""view""
  },
  {
    ""type"": ""function"",
    ""name"": ""swap"",
    ""inputs"": [
      {
        ""name"": ""params"",
        ""type"": ""tuple"",
        ""internalType"": ""struct SwapTypes.SwapParams"",
        ""components"": [
          {
            ""name"": ""swapId"",
            ""type"": ""bytes32"",
            ""internalType"": ""bytes32""
          },
          {
            ""name"": ""dstEid"",
            ""type"": ""uint32"",
            ""internalType"": ""uint32""
          },
          {
            ""name"": ""tokenIn"",
            ""type"": ""address"",
            ""internalType"": ""address""
          },
          {
            ""name"": ""tokenOut"",
            ""type"": ""address"",
            ""internalType"": ""address""
          },
          {
            ""name"": ""amountIn"",
            ""type"": ""uint256"",
            ""internalType"": ""uint256""
          },
          {
            ""name"": ""minAmountOut"",
            ""type"": ""uint256"",
            ""internalType"": ""uint256""
          },
          {
            ""name"": ""receiver"",
            ""type"": ""address"",
            ""internalType"": ""address""
          },
          {
            ""name"": ""path"",
            ""type"": ""address[]"",
            ""internalType"": ""address[]""
          }
        ]
      },
      {
        ""name"": ""sendOptions"",
        ""type"": ""bytes"",
        ""internalType"": ""bytes""
      },
      {
        ""name"": ""returnOptions"",
        ""type"": ""bytes"",
        ""internalType"": ""bytes""
      },
      {
        ""name"": ""returnNativeDrop"",
        ""type"": ""uint128"",
        ""internalType"": ""uint128""
      }
    ],
    ""outputs"": [],
    ""stateMutability"": ""payable""
  },
  {
    ""type"": ""function"",
    ""name"": ""claimTokenRefund"",
    ""inputs"": [
      {
        ""name"": ""swapId"",
        ""type"": ""bytes32"",
        ""internalType"": ""bytes32""
      }
    ],
    ""outputs"": [],
    ""stateMutability"": ""nonpayable""
  },
  {
    ""type"": ""function"",
    ""name"": ""getRefund"",
    ""inputs"": [
      {
        ""name"": ""swapId"",
        ""type"": ""bytes32"",
        ""internalType"": ""bytes32""
      }
    ],
    ""outputs"": [
      {
        ""name"": ""recipient"",
        ""type"": ""address"",
        ""internalType"": ""address""
      },
      {
        ""name"": ""token"",
        ""type"": ""address"",
        ""internalType"": ""address""
      },
      {
        ""name"": ""amount"",
        ""type"": ""uint256"",
        ""internalType"": ""uint256""
      }
    ],
    ""stateMutability"": ""view""
  },
  {
    ""type"": ""event"",
    ""name"": ""SwapInitiated"",
    ""inputs"": [
      {
        ""name"": ""swapId"",
        ""type"": ""bytes32"",
        ""indexed"": false,
        ""internalType"": ""bytes32""
      },
      {
        ""name"": ""dstEid"",
        ""type"": ""uint32"",
        ""indexed"": false,
        ""internalType"": ""uint32""
      },
      {
        ""name"": ""tokenIn"",
        ""type"": ""address"",
        ""indexed"": false,
        ""internalType"": ""address""
      },
      {
        ""name"": ""tokenOut"",
        ""type"": ""address"",
        ""indexed"": false,
        ""internalType"": ""address""
      },
      {
        ""name"": ""amountIn"",
        ""type"": ""uint256"",
        ""indexed"": false,
        ""internalType"": ""uint256""
      },
      {
        ""name"": ""amountOut"",
        ""type"": ""uint256"",
        ""indexed"": false,
        ""internalType"": ""uint256""
      },
      {
        ""name"": ""receiver"",
        ""type"": ""address"",
        ""indexed"": false,
        ""internalType"": ""address""
      },
      {
        ""name"": ""fee"",
        ""type"": ""uint256"",
        ""indexed"": false,
        ""internalType"": ""uint256""
      }
    ],
    ""anonymous"": false
  },
  {
    ""type"": ""event"",
    ""name"": ""SwapCompleted"",
    ""inputs"": [
      {
        ""name"": ""swapId"",
        ""type"": ""bytes32"",
        ""indexed"": false,
        ""internalType"": ""bytes32""
      }
    ],
    ""anonymous"": false
  },
  {
    ""type"": ""event"",
    ""name"": ""SwapExecuted"",
    ""inputs"": [
      {
        ""name"": ""swapId"",
        ""type"": ""bytes32"",
        ""indexed"": false,
        ""internalType"": ""bytes32""
      },
      {
        ""name"": ""tokenOut"",
        ""type"": ""address"",
        ""indexed"": false,
        ""internalType"": ""address""
      },
      {
        ""name"": ""amountOut"",
        ""type"": ""uint256"",
        ""indexed"": false,
        ""internalType"": ""uint256""
      },
      {
        ""name"": ""receiver"",
        ""type"": ""address"",
        ""indexed"": false,
        ""internalType"": ""address""
      }
    ],
    ""anonymous"": false
  },
  {
    ""type"": ""event"",
    ""name"": ""SwapFailed"",
    ""inputs"": [
      {
        ""name"": ""swapId"",
        ""type"": ""bytes32"",
        ""indexed"": false,
        ""internalType"": ""bytes32""
      },
      {
        ""name"": ""tokenOut"",
        ""type"": ""address"",
        ""indexed"": false,
        ""internalType"": ""address""
      },
      {
        ""name"": ""amountOut"",
        ""type"": ""uint256"",
        ""indexed"": false,
        ""internalType"": ""uint256""
      },
      {
        ""name"": ""receiver"",
        ""type"": ""address"",
        ""indexed"": false,
        ""internalType"": ""address""
      }
    ],
    ""anonymous"": false
  },
  {
    ""type"": ""event"",
    ""name"": ""SwapRefunded"",
    ""inputs"": [
      {
        ""name"": ""swapId"",
        ""type"": ""bytes32"",
        ""indexed"": true,
        ""internalType"": ""bytes32""
      },
      {
        ""name"": ""token"",
        ""type"": ""address"",
        ""indexed"": true,
        ""internalType"": ""address""
      },
      {
        ""name"": ""amount"",
        ""type"": ""uint256"",
        ""indexed"": false,
        ""internalType"": ""uint256""
      },
      {
        ""name"": ""user"",
        ""type"": ""address"",
        ""indexed"": true,
        ""internalType"": ""address""
      }
    ],
    ""anonymous"": false
  },
  {
    ""type"": ""event"",
    ""name"": ""TokenRefundClaimed"",
    ""inputs"": [
      {
        ""name"": ""swapId"",
        ""type"": ""bytes32"",
        ""indexed"": true,
        ""internalType"": ""bytes32""
      },
      {
        ""name"": ""token"",
        ""type"": ""address"",
        ""indexed"": true,
        ""internalType"": ""address""
      },
      {
        ""name"": ""recipient"",
        ""type"": ""address"",
        ""indexed"": true,
        ""internalType"": ""address""
      },
      {
        ""name"": ""amount"",
        ""type"": ""uint256"",
        ""indexed"": false,
        ""internalType"": ""uint256""
      }
    ],
    ""anonymous"": false
  }
]
";

        public const string ERC20Abi = @"[
            { 'constant':true,'inputs':[{'name':'_owner','type':'address'}],'name':'balanceOf','outputs':[{'name':'balance','type':'uint256'}],'type':'function' },
            { 'constant':true,'inputs':[],'name':'decimals','outputs':[{'name':'','type':'uint8'}],'type':'function' },
            { 'constant':true,'inputs':[],'name':'symbol','outputs':[{'name':'','type':'string'}],'type':'function' }
        ]";

        public const string StakeAbi = @"
[
    {
        ""type"": ""function"",
        ""name"": ""deposit"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""token"",
                ""type"": ""address"",
                ""internalType"": ""address""
            },
            {
                ""name"": ""amount"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""lockDuration"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""function"",
        ""name"": ""getDeposit"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [
            {
                ""name"": """",
                ""type"": ""tuple"",
                ""internalType"": ""struct StakingTypes.Deposit"",
                ""components"": [
                    {
                        ""name"": ""depositId"",
                        ""type"": ""bytes32"",
                        ""internalType"": ""bytes32""
                    },
                    {
                        ""name"": ""depositor"",
                        ""type"": ""address"",
                        ""internalType"": ""address""
                    },
                    {
                        ""name"": ""token"",
                        ""type"": ""address"",
                        ""internalType"": ""address""
                    },
                    {
                        ""name"": ""principal"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""profit"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""profitRateBps"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""lockDuration"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""depositedAt"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""unlocksAt"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""status"",
                        ""type"": ""uint8"",
                        ""internalType"": ""enum StakingTypes.DepositStatus""
                    },
                    {
                        ""name"": ""profitWithdrawn"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    }
                ]
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""function"",
        ""name"": ""getPlan"",
        ""inputs"": [
            {
                ""name"": ""planId"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            }
        ],
        ""outputs"": [
            {
                ""name"": """",
                ""type"": ""tuple"",
                ""internalType"": ""struct StakingTypes.Plan"",
                ""components"": [
                    {
                        ""name"": ""minDuration"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""maxDuration"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""profitRateBps"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""active"",
                        ""type"": ""bool"",
                        ""internalType"": ""bool""
                    }
                ]
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""function"",
        ""name"": ""previewAccruedProfit"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [
            {
                ""name"": ""claimable"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""function"",
        ""name"": ""previewEarlyWithdraw"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""amount"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            }
        ],
        ""outputs"": [
            {
                ""name"": ""payout"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""earlyProfit"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""earlyRateBps"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""function"",
        ""name"": ""previewPayout"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [
            {
                ""name"": ""totalPayout"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""function"",
        ""name"": ""withdrawAll"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""function"",
        ""name"": ""withdrawEarly"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""amount"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""function"",
        ""name"": ""withdrawProfit"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""event"",
        ""name"": ""DepositCreated"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""indexed"": false,
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""depositor"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""token"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""principal"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""profit"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""lockDuration"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""unlocksAt"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            }
        ],
        ""anonymous"": false
    },
    {
        ""type"": ""event"",
        ""name"": ""ProfitWithdrawn"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""indexed"": false,
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""depositor"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""token"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""profit"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            }
        ],
        ""anonymous"": false
    },
    {
        ""type"": ""event"",
        ""name"": ""Withdrawn"",
        ""inputs"": [
            {
                ""name"": ""depositId"",
                ""type"": ""bytes32"",
                ""indexed"": false,
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""depositor"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""token"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""principal"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""profit"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""totalPayout"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            }
        ],
        ""anonymous"": false
    }
]";

    }


}
